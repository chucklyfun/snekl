using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Snekl.Api.Controllers;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;
using Snekl.Tests.Shared;
using AccountDtos = Snekl.Account.Domain.Dtos;
using ContentDtos = Snekl.Domain.Dtos;


namespace Snekl.Api.Tests;

[CollectionDefinition(Name)]
public class DualApiCollection : ICollectionFixture<DualApiFixture>
{
    public const string Name = "DualApi";
}

/// <summary>
/// Boots Auth + Content against one Postgres container and routes JWKS fetches
/// from Content to the Auth TestServer.
/// </summary>
public sealed class DualApiFixture : IAsyncLifetime
{
    public const string AuthBaseUrl = "http://auth.test";
    public const string ContentBaseUrl = "http://content.test";

    private readonly PostgresFixture _postgres = new();

    public WebApplicationFactory<Snekl.Account.Api.Controllers.AuthController> AuthFactory { get; private set; } = null!;
    public WebApplicationFactory<ForumsController> ContentFactory { get; private set; } = null!;

    public HttpClient AuthClient => CreateAuthClient();
    public HttpClient ContentClient => CreateContentClient();

    public HttpClient CreateAuthClient() => AuthFactory.CreateDefaultClient(new MemberDeviceHandler());

    public HttpClient CreateContentClient() => ContentFactory.CreateDefaultClient(new MemberDeviceHandler());

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        await PostgresDatabase.EnsureCreatedAsync(_postgres.ConnectionString, "snekl_dual_auth");
        await PostgresDatabase.EnsureCreatedAsync(_postgres.ConnectionString, "snekl_dual_content");

        var authCs = _postgres.ConnectionStringForDatabase("snekl_dual_auth");
        var contentCs = _postgres.ConnectionStringForDatabase("snekl_dual_content");

        var contentRelay = new TestServerRoutingHandler(ContentBaseUrl);
        AuthFactory = new WebApplicationFactory<Snekl.Account.Api.Controllers.AuthController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", authCs);
            builder.UseSetting("ConnectionStrings:Redis", "");
            builder.UseSetting("Auth:BaseUrl", AuthBaseUrl);
            builder.UseSetting("Auth:Name", "Snekl Auth Dual");
            builder.UseSetting("Auth:SigningKey", "snekl-dual-auth-signing-key-min-32-chars!");
            builder.UseSetting("Email:AutoVerify", "true");
            builder.UseSetting("RateLimiting:Enabled", "false");
            builder.UseSetting("Snekl:ResetDb", "true");
            builder.UseSetting("Frontend:BaseUrl", "http://localhost:5173");
            builder.ConfigureTestServices(services =>
            {
                services.Configure<HttpClientFactoryOptions>("extension-install", options =>
                {
                    options.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = contentRelay);
                });
            });
        });

        // Start auth host so CreateHandler works.
        _ = AuthClient;
        var authHandler = AuthFactory.Server.CreateHandler();

        ContentFactory = new WebApplicationFactory<ForumsController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", contentCs);
            builder.UseSetting("ConnectionStrings:Redis", "");
            builder.UseSetting("RateLimiting:Enabled", "false");
            builder.UseSetting("Snekl:ResetDb", "true");
            builder.UseSetting("Node:Name", "Snekl Dual Content");
            builder.UseSetting("Node:BaseUrl", ContentBaseUrl);
            builder.UseSetting("Node:Description", "Dual test node");
            builder.UseSetting("Jwt:Key", "snekl-dual-content-secret-key-min-32-chars");
            builder.UseSetting("Jwt:Issuer", "snekl");
            builder.UseSetting("Jwt:Audience", "snekl");
            builder.UseSetting("AuthIssuers:0:Name", "Snekl Auth Dual");
            builder.UseSetting("AuthIssuers:0:IssuerUrl", AuthBaseUrl);
            builder.UseSetting("AuthIssuers:0:JwksUrl", $"{AuthBaseUrl}/.well-known/jwks.json");
            builder.UseSetting("ObjectStorage:Provider", "InMemory");
            builder.UseSetting("ObjectStorage:MaxUploadBytes", "10485760");
            builder.ConfigureTestServices(services =>
            {
                services.ConfigureAll<HttpClientFactoryOptions>(options =>
                {
                    options.HttpMessageHandlerBuilderActions.Add(b =>
                    {
                        b.PrimaryHandler = new AuthJwksRoutingHandler(AuthBaseUrl, authHandler);
                    });
                });
            });
        });

        _ = ContentClient;
        contentRelay.Target = ContentFactory.Server.CreateHandler();
    }

    public async Task GrantSuperUserAsync(Guid userId)
    {
        await using var scope = ContentFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SneklDbContext>();
        if (!await db.SuperUserGrants.AnyAsync(g => g.UserId == userId))
        {
            db.SuperUserGrants.Add(new SuperUserGrant { UserId = userId, Note = "test" });
            await db.SaveChangesAsync();
        }
    }

    public async Task GrantMemberRoleAsync(Guid userId)
    {
        await using var scope = ContentFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SneklDbContext>();
        var member = await db.ServerRoles.FirstAsync(r => r.Name == "Member");
        if (!await db.ServerRoleAssignments.AnyAsync(a => a.UserId == userId && a.RoleId == member.Id))
        {
            db.ServerRoleAssignments.Add(new ServerRoleAssignment
            {
                UserId = userId,
                RoleId = member.Id
            });
            await db.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await ContentFactory.DisposeAsync();
        await AuthFactory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[Collection(DualApiCollection.Name)]
public class AuthToContentWorkflowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DualApiFixture _fixture;

    public AuthToContentWorkflowTests(DualApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task RegisterOnAuth_UsesJwtOnContent_CreatesForumAndPost()
    {
        var username = $"x_{Guid.NewGuid():N}"[..12];
        var email = $"{username}@example.com";
        var (keys, proof) = DidProofHelper.CreateRegistrationProof(username);

        var register = await _fixture.AuthClient.PostAsJsonAsync("/api/auth/register", new AccountDtos.RegisterRequest(
            username,
            email,
            "Password1!",
            "Cross User",
            keys.Did,
            proof));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var session = await register.Content.ReadFromJsonAsync<AccountDtos.RingSessionDto>(JsonOptions);
        Assert.NotNull(session);

        using var content = _fixture.CreateContentClient();
        content.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var me = await content.GetAsync("/api/auth/me");
        var meText = await me.Content.ReadAsStringAsync();
        Assert.True(me.IsSuccessStatusCode, $"GET /api/auth/me failed: {(int)me.StatusCode} {meText}");
        var meBody = JsonSerializer.Deserialize<ContentDtos.AuthMeResponse>(meText, JsonOptions);
        Assert.NotNull(meBody);
        Assert.Equal(username, meBody!.User.Username);

        await _fixture.GrantMemberRoleAsync(meBody.User.Id);

        var forumResponse = await content.PostAsJsonAsync("/api/forums", new ContentDtos.CreateForumRequest(
            "Cross Forum",
            $"cross-{Guid.NewGuid():N}"[..16],
            "From auth JWT"));
        Assert.Equal(HttpStatusCode.Created, forumResponse.StatusCode);
        var forum = await forumResponse.Content.ReadFromJsonAsync<ContentDtos.ForumDto>(JsonOptions);
        Assert.NotNull(forum);

        var postResponse = await content.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            forum!.Id,
            null,
            "Federated-identity post",
            "Federated title"));
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
    }

    [Fact]
    public async Task FollowingOnly_AllowsFollowedUser_AndRejectsOthers()
    {
        async Task<(AccountDtos.RingSessionDto Session, ContentDtos.AuthMeResponse Me)> RegisterContentUserAsync()
        {
            var username = $"f_{Guid.NewGuid():N}"[..12];
            var email = $"{username}@example.com";
            var (keys, proof) = DidProofHelper.CreateRegistrationProof(username);
            var register = await _fixture.AuthClient.PostAsJsonAsync("/api/auth/register", new AccountDtos.RegisterRequest(
                username,
                email,
                "Password1!",
                "Follow User",
                keys.Did,
                proof));
            Assert.Equal(HttpStatusCode.OK, register.StatusCode);
            var session = await register.Content.ReadFromJsonAsync<AccountDtos.RingSessionDto>(JsonOptions);
            Assert.NotNull(session);

            using var content = _fixture.CreateContentClient();
            content.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", session!.AccessToken);
            var me = await content.GetFromJsonAsync<ContentDtos.AuthMeResponse>("/api/auth/me", JsonOptions);
            Assert.NotNull(me);
            await _fixture.GrantMemberRoleAsync(me!.User.Id);
            return (session, me);
        }

        var (opSession, _) = await RegisterContentUserAsync();
        var (friendSession, _) = await RegisterContentUserAsync();
        var (strangerSession, _) = await RegisterContentUserAsync();

        using var authOp = _fixture.CreateAuthClient();
        authOp.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", opSession.AccessToken);
        var follow = await authOp.PostAsJsonAsync("/api/me/follows", new AccountDtos.UpsertFollowRequest(friendSession.Person.Did));
        Assert.Equal(HttpStatusCode.OK, follow.StatusCode);

        using var op = _fixture.CreateContentClient();
        op.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opSession.AccessToken);
        using var friend = _fixture.CreateContentClient();
        friend.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", friendSession.AccessToken);
        using var stranger = _fixture.CreateContentClient();
        stranger.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", strangerSession.AccessToken);

        var forumResponse = await op.PostAsJsonAsync("/api/forums", new ContentDtos.CreateForumRequest(
            "Follow Forum",
            $"ff-{Guid.NewGuid():N}"[..16],
            "Reply policy"));
        Assert.Equal(HttpStatusCode.Created, forumResponse.StatusCode);
        var forum = await forumResponse.Content.ReadFromJsonAsync<ContentDtos.ForumDto>(JsonOptions);
        Assert.NotNull(forum);

        var postResponse = await op.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            forum!.Id,
            null,
            "Only people I follow",
            "Closed",
            ReplyPolicy: "following"));
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var post = await postResponse.Content.ReadFromJsonAsync<ContentDtos.PostDto>(JsonOptions);
        Assert.NotNull(post);

        var friendReply = await friend.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            forum.Id,
            post!.Id,
            "I am followed"));
        Assert.Equal(HttpStatusCode.Created, friendReply.StatusCode);

        var strangerReply = await stranger.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            forum.Id,
            post.Id,
            "I am not followed"));
        Assert.Equal(HttpStatusCode.BadRequest, strangerReply.StatusCode);

        var strangerThread = await stranger.GetFromJsonAsync<ContentDtos.PostDto>($"/api/posts/{post.Id}", JsonOptions);
        Assert.NotNull(strangerThread);
        Assert.False(strangerThread.ViewerActions?.CanReply);

        var friendThread = await friend.GetFromJsonAsync<ContentDtos.PostDto>($"/api/posts/{post.Id}", JsonOptions);
        Assert.NotNull(friendThread);
        Assert.True(friendThread.ViewerActions?.CanReply);
    }
}
