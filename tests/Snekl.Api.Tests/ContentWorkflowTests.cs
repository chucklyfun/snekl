using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Snekl.Domain.Dtos;

namespace Snekl.Api.Tests;

[Collection(ContentApiCollection.Name)]
public class ContentWorkflowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ContentApiFixture _fixture;

    public ContentWorkflowTests(ContentApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Register_CreateForum_Post_Reply_Upvote()
    {
        var username = $"c_{Guid.NewGuid():N}"[..12];
        var register = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username,
            $"{username}@example.com",
            "Password1!",
            "Content User"));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        Assert.NotNull(auth);

        await _fixture.GrantMemberRoleAsync(auth!.User.Id);

        using var authed = CreateAuthedClient(auth.Token);

        var forumResponse = await authed.PostAsJsonAsync("/api/forums", new CreateForumRequest(
            "General",
            $"general-{Guid.NewGuid():N}"[..18],
            "A test forum"));
        Assert.Equal(HttpStatusCode.Created, forumResponse.StatusCode);
        var forum = await forumResponse.Content.ReadFromJsonAsync<ForumDto>(JsonOptions);
        Assert.NotNull(forum);
        Assert.True(forum!.ViewerIsMember);
        Assert.True(forum.ViewerCanManage);

        var postResponse = await authed.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            forum!.Id,
            null,
            "Hello thread",
            "Hello title"));
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var post = await postResponse.Content.ReadFromJsonAsync<PostDto>(JsonOptions);
        Assert.NotNull(post);
        Assert.Equal("Hello thread", post!.Message);
        Assert.Equal("Hello title", post.Title);

        var replyResponse = await authed.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            forum.Id,
            post.Id,
            "A reply"));
        Assert.Equal(HttpStatusCode.Created, replyResponse.StatusCode);

        var selfUpvote = await authed.PostAsync($"/api/posts/{post.Id}/upvote", null);
        Assert.Equal(HttpStatusCode.BadRequest, selfUpvote.StatusCode);

        var voterUsername = $"v_{Guid.NewGuid():N}"[..12];
        var voterRegister = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            voterUsername,
            $"{voterUsername}@example.com",
            "Password1!",
            "Voter"));
        Assert.Equal(HttpStatusCode.OK, voterRegister.StatusCode);
        var voterAuth = await voterRegister.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        Assert.NotNull(voterAuth);
        await _fixture.GrantMemberRoleAsync(voterAuth!.User.Id);
        using var voterClient = CreateAuthedClient(voterAuth.Token);

        var upvoteResponse = await voterClient.PostAsync($"/api/posts/{post.Id}/upvote", null);
        Assert.Equal(HttpStatusCode.OK, upvoteResponse.StatusCode);
        var upvote = await upvoteResponse.Content.ReadFromJsonAsync<UpvoteResultDto>(JsonOptions);
        Assert.NotNull(upvote);
        Assert.True(upvote!.ViewerHasUpvoted);
        Assert.Equal(1, upvote.UpvoteCount);

        var threadResponse = await authed.GetAsync($"/api/posts/{post.Id}");
        Assert.Equal(HttpStatusCode.OK, threadResponse.StatusCode);
        var thread = await threadResponse.Content.ReadFromJsonAsync<PostDto>(JsonOptions);
        Assert.NotNull(thread);
        Assert.Equal(1, thread!.ReplyCount);
    }

    [Fact]
    public async Task FollowingOnly_AllowsOpReply_AndRejectsStranger()
    {
        async Task<AuthResponse> RegisterAsync(string prefix)
        {
            var username = $"{prefix}_{Guid.NewGuid():N}"[..12];
            var register = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
                username,
                $"{username}@example.com",
                "Password1!",
                "User"));
            Assert.Equal(HttpStatusCode.OK, register.StatusCode);
            var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
            Assert.NotNull(auth);
            await _fixture.GrantMemberRoleAsync(auth!.User.Id);
            return auth;
        }

        var op = await RegisterAsync("op");
        var stranger = await RegisterAsync("st");
        using var opClient = CreateAuthedClient(op.Token);
        using var strangerClient = CreateAuthedClient(stranger.Token);

        var forumResponse = await opClient.PostAsJsonAsync("/api/forums", new CreateForumRequest(
            "Following Forum",
            $"follow-{Guid.NewGuid():N}"[..18],
            "Reply policy"));
        Assert.Equal(HttpStatusCode.Created, forumResponse.StatusCode);
        var forum = await forumResponse.Content.ReadFromJsonAsync<ForumDto>(JsonOptions);
        Assert.NotNull(forum);

        var postResponse = await opClient.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            forum!.Id,
            null,
            "Only my follows",
            "Closed replies",
            ReplyPolicy: "following"));
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var post = await postResponse.Content.ReadFromJsonAsync<PostDto>(JsonOptions);
        Assert.NotNull(post);
        Assert.Equal("following", post!.ReplyPolicy);
        Assert.True(post.ViewerActions?.CanReply);
        Assert.True(post.ViewerActions?.CanSetReplyPolicy);

        var opReply = await opClient.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            forum.Id,
            post.Id,
            "OP can always reply"));
        Assert.Equal(HttpStatusCode.Created, opReply.StatusCode);

        var strangerReply = await strangerClient.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            forum.Id,
            post.Id,
            "Should be blocked"));
        Assert.Equal(HttpStatusCode.BadRequest, strangerReply.StatusCode);
    }

    [Fact]
    public async Task SiteAdmin_Login_And_GetNode()
    {
        var username = $"a_{Guid.NewGuid():N}"[..12];
        var register = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username,
            $"{username}@example.com",
            "Password1!",
            "Admin User"));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        Assert.NotNull(auth);

        using var member = CreateAuthedClient(auth!.Token);
        var status = await member.GetFromJsonAsync<OperatorStatusDto>("/api/admin/operator-status", JsonOptions);
        Assert.NotNull(status);
        Assert.Equal(OperatorStates.Unclaimed, status.State);

        var login = await member.PostAsJsonAsync(
            "/api/admin/login",
            new OperatorLoginRequest(NewAdminPassword: "Operator1!"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var adminAuth = await login.Content.ReadFromJsonAsync<SiteAdminAuthResponse>(JsonOptions);
        Assert.NotNull(adminAuth);
        Assert.False(string.IsNullOrWhiteSpace(adminAuth!.Token));

        var me = await member.GetFromJsonAsync<AuthMeResponse>("/api/auth/me", JsonOptions);
        Assert.NotNull(me);
        Assert.True(me.IsSuperUser);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/site/node");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth.Token);
        var nodeResponse = await _fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, nodeResponse.StatusCode);
    }

    [Fact]
    public async Task AdminBootstrap_ReturnsContentKind()
    {
        var response = await _fixture.Client.GetAsync("/server/bootstrap");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bootstrap = await response.Content.ReadFromJsonAsync<AdminBootstrapDto>(JsonOptions);
        Assert.NotNull(bootstrap);
        Assert.Equal("content", bootstrap.Kind);
    }

    [Fact]
    public async Task PublicNodeAuth_ListsTrustedIssuersWithoutServerHeader()
    {
        var response = await _fixture.Client.GetAsync("/api/node/auth");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<NodeAuthInfoDto>(JsonOptions);
        Assert.NotNull(auth);
        Assert.Contains(auth!.Issuers, i => i.Name == "Unused" && i.IssuerUrl == "http://unused.invalid");
    }

    [Fact]
    public async Task PublicNodeEndpoint_ReturnsLocalNode()
    {
        var response = await _fixture.Client.GetAsync("/api/node");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var node = await response.Content.ReadFromJsonAsync<NodeInfoDto>(JsonOptions);
        Assert.NotNull(node);
        Assert.Equal("http://content.test", node!.BaseUrl);
        Assert.NotNull(node.RateLimiting);
        Assert.Equal("ip", node.RateLimiting!.Partition);
        Assert.Contains(
            node.RateLimiting.Rules,
            r => r.PathPrefix == "/api/posts" && r.Method == "POST" && r.Limit == 30 && r.WindowSeconds == 60);
    }

    private HttpClient CreateAuthedClient(string token)
    {
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
