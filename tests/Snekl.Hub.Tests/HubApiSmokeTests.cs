using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Snekl.Hub.Controllers;
using Snekl.Hub.Domain.Dtos;
using Snekl.Hub.Infrastructure.Services;
using Snekl.Tests.Shared;

namespace Snekl.Hub.Tests;

[CollectionDefinition(Name)]
public class HubApiCollection : ICollectionFixture<HubApiFixture>
{
    public const string Name = "HubApi";
}

public sealed class HubApiFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public WebApplicationFactory<HubController> Factory { get; private set; } = null!;

    public HttpClient Client => Factory.CreateClient();

    public HttpClient CreateMemberClient(string? did = null, string? username = null)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueTestToken(did, username));
        return client;
    }

    public static string IssueTestToken(string? did = null, string? username = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("snekl-auth-test-signing-key-min-32-chars!!"));
        var token = new JwtSecurityToken(
            issuer: "http://auth.test",
            audience: "snekl",
            claims:
            [
                new Claim("did", did ?? "did:key:test-requester"),
                new Claim("username", username ?? "requester"),
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<HttpClient> CreateOperatorClientAsync(string? did = null, string? username = null)
    {
        const string password = "Operator1!";
        var memberToken = IssueTestToken(did, username);
        using var member = Factory.CreateClient();
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);

        var status = await member.GetFromJsonAsync<HubOperatorStatusDto>("/api/hub/admin/status")
            ?? throw new InvalidOperationException("Missing operator status.");
        var body = status.State is "unclaimed" or "setupRequired"
            ? new HubOperatorLoginRequest(NewAdminPassword: password)
            : new HubOperatorLoginRequest(AdminPassword: password);
        var login = await member.PostAsJsonAsync("/api/hub/admin/login", body);
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<HubAdminAuthResponse>()
            ?? throw new InvalidOperationException("Missing hub admin token.");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Snekl-Member-Authorization",
            "Bearer " + memberToken);
        return client;
    }

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        const string dbName = "snekl_hub_tests";
        await PostgresDatabase.EnsureCreatedAsync(_postgres.ConnectionString, dbName);
        var connectionString = _postgres.ConnectionStringForDatabase(dbName);

        Factory = new WebApplicationFactory<HubController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Hub", connectionString);
            builder.UseSetting("Hub:Name", "Snekl Hub Test");
            builder.UseSetting("Hub:BaseUrl", "http://hub.test");
            builder.UseSetting("Hub:AuthBaseUrl", "http://auth.test");
            builder.UseSetting("Hub:AuthSigningKey", "snekl-auth-test-signing-key-min-32-chars!!");
            builder.UseSetting("Hub:AdminSigningKey", "snekl-hub-admin-test-signing-key-min-32!!");
            builder.UseSetting("Snekl:ResetDb", "true");
            builder.UseSetting("Hub:SyncOriginsOnStart", "false");
            builder.UseSetting("Hub:RequireSignatures", "false");
            builder.UseSetting("ConnectionStrings:Redis", "");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IOriginJoinClient, FakeOriginJoinClient>();
                services.AddSingleton<IOriginRecentClient, FakeOriginRecentClient>();
            });
        });

        _ = Client;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

public sealed class FakeOriginRecentClient : IOriginRecentClient
{
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<OriginRecentThread>> Threads =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<OriginRecentThread>> GetRecentAsync(
        string originBaseUrl,
        int limit,
        CancellationToken ct = default)
    {
        var key = originBaseUrl.Trim().TrimEnd('/');
        if (Threads.TryGetValue(key, out var list))
            return Task.FromResult(list);
        return Task.FromResult<IReadOnlyList<OriginRecentThread>>([]);
    }
}

public sealed class FakeOriginJoinClient : IOriginJoinClient
{
    public Task<Guid> RequestInviteAsync(
        string originBaseUrl,
        string authorizationHeader,
        string hubBaseUrl,
        string hubName,
        string? deviceProof,
        CancellationToken ct = default) =>
        Task.FromResult(Guid.NewGuid());
}

[Collection(HubApiCollection.Name)]
public class HubApiSmokeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HubApiFixture _fixture;

    public HubApiSmokeTests(HubApiFixture fixture) => _fixture = fixture;

    private Task<HttpClient> Authed() => _fixture.CreateOperatorClientAsync();

    [Fact]
    public async Task GetHub_ReturnsInfoWithMapVersion()
    {
        var info = await _fixture.Client.GetFromJsonAsync<HubInfoDto>("/api/hub", JsonOptions);
        Assert.NotNull(info);
        Assert.Equal("Snekl Hub Test", info.Name);
        Assert.Equal("http://hub.test", info.BaseUrl);
        Assert.Equal("http://auth.test", info.AuthBaseUrl);
        Assert.True(info.MapVersion >= 1);
    }

    [Fact]
    public async Task FederationConfig_WellKnownAndApiMatch()
    {
        var wellKnown = await _fixture.Client.GetFromJsonAsync<FederationConfigDto>(
            "/.well-known/snekl-federation.json", JsonOptions);
        var api = await _fixture.Client.GetFromJsonAsync<FederationConfigDto>("/api/hub/config", JsonOptions);
        Assert.NotNull(wellKnown);
        Assert.NotNull(api);
        Assert.Equal(FederationConfigSchema.V1, wellKnown.Schema);
        Assert.Equal(wellKnown.Name, api.Name);
        Assert.Equal("http://hub.test", wellKnown.BaseUrl);
        Assert.Equal("http://auth.test", wellKnown.Auth.Issuer);
        Assert.Equal("http://auth.test/.well-known/jwks.json", wellKnown.Auth.Jwks);
        Assert.Equal("http://hub.test/api/hub/feed", wellKnown.Feed);
    }

    [Fact]
    public async Task HubAdminBootstrap_ReturnsHubKind()
    {
        var bootstrap = await _fixture.Client.GetFromJsonAsync<AdminBootstrapDto>("/server/bootstrap", JsonOptions);
        Assert.NotNull(bootstrap);
        Assert.Equal("hub", bootstrap.Kind);
        Assert.Equal("http://auth.test", bootstrap.AuthLoginUrl);
    }

    [Fact]
    public async Task RegisterServer_UnauthorizedWithoutJwt()
    {
        var register = await _fixture.Client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest("did:web:anon.test", "http://anon.test", "Anon"));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, register.StatusCode);
    }

    [Fact]
    public async Task ListJoinRequests_UnauthorizedWithoutJwt()
    {
        var requests = await _fixture.Client.GetAsync("/api/hub/join-requests");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, requests.StatusCode);
    }

    [Fact]
    public async Task RegisterServer_MemberJwtAlone_IsUnauthorized()
    {
        using var client = _fixture.CreateMemberClient();
        var register = await client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest("did:web:member-only.test", "http://member-only.test", "Member Only"));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, register.StatusCode);
    }

    [Fact]
    public async Task OperatorStatus_SecondUser_IsForbiddenAfterClaim()
    {
        using var _ = await Authed();
        using var other = _fixture.CreateMemberClient("did:key:other-person", "other");
        var status = await other.GetFromJsonAsync<HubOperatorStatusDto>("/api/hub/admin/status", JsonOptions);
        Assert.NotNull(status);
        Assert.Equal("forbidden", status.State);
    }

    [Fact]
    public async Task RegisterServer_IsPendingUntilOriginAccepts()
    {
        using var client = await Authed();
        var before = await client.GetFromJsonAsync<HubInfoDto>("/api/hub", JsonOptions);
        Assert.NotNull(before);

        var register = await client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest("did:web:origin.test", "http://origin.test", "Origin Test"));
        register.EnsureSuccessStatusCode();

        var pending = await register.Content.ReadFromJsonAsync<RegisteredServerDto>(JsonOptions);
        Assert.NotNull(pending);
        Assert.Equal("pending", pending.Status);
        Assert.Equal("hub", pending.InitiatedBy);
        Assert.NotNull(pending.OriginInviteId);

        var mapAfterRequest = await client.GetFromJsonAsync<FederationMapDto>("/api/hub/map", JsonOptions);
        Assert.NotNull(mapAfterRequest);
        Assert.Equal(before.MapVersion, mapAfterRequest.MapVersion);
        Assert.DoesNotContain(mapAfterRequest.Servers, s => s.ServerDid == "did:web:origin.test");

        var requests = await client.GetFromJsonAsync<List<RegisteredServerDto>>("/api/hub/join-requests", JsonOptions);
        Assert.NotNull(requests);
        Assert.Contains(requests, s => s.ServerDid == "did:web:origin.test" && s.Status == "pending");

        var accept = await client.PostAsJsonAsync(
            "/api/hub/join-requests/accept",
            new AcceptJoinRequest("did:web:origin.test", "origin", pending.OriginInviteId));
        accept.EnsureSuccessStatusCode();

        var map = await client.GetFromJsonAsync<FederationMapDto>("/api/hub/map", JsonOptions);
        Assert.NotNull(map);
        Assert.True(map.MapVersion > before.MapVersion);
        Assert.Contains(map.Servers, s => s.ServerDid == "did:web:origin.test");
    }

    [Fact]
    public async Task RegisterServer_SecondDistinctOrigin_DoesNotReplaceFirst()
    {
        using var client = await Authed();
        var first = await client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest("did:web:alpha.test", "http://alpha.test", "Alpha"));
        first.EnsureSuccessStatusCode();
        var firstDto = await first.Content.ReadFromJsonAsync<RegisteredServerDto>(JsonOptions);

        var second = await client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest("did:web:beta.test", "http://beta.test", "Beta"));
        second.EnsureSuccessStatusCode();
        var secondDto = await second.Content.ReadFromJsonAsync<RegisteredServerDto>(JsonOptions);

        (await client.PostAsJsonAsync(
            "/api/hub/join-requests/accept",
            new AcceptJoinRequest("did:web:alpha.test", "origin", firstDto!.OriginInviteId))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(
            "/api/hub/join-requests/accept",
            new AcceptJoinRequest("did:web:beta.test", "origin", secondDto!.OriginInviteId))).EnsureSuccessStatusCode();

        var map = await client.GetFromJsonAsync<FederationMapDto>("/api/hub/map", JsonOptions);
        Assert.NotNull(map);
        Assert.Contains(map.Servers, s => s.ServerDid == "did:web:alpha.test" && s.Name == "Alpha");
        Assert.Contains(map.Servers, s => s.ServerDid == "did:web:beta.test" && s.Name == "Beta");
    }

    [Fact]
    public async Task RegisterServer_DidAndUrlFromDifferentServers_ReturnsBadRequest()
    {
        using var client = await Authed();
        (await client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest("did:web:keep-alpha.test", "http://keep-alpha.test", "Keep Alpha")))
            .EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest("did:web:keep-beta.test", "http://keep-beta.test", "Keep Beta")))
            .EnsureSuccessStatusCode();

        var mixed = await client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest("did:web:keep-alpha.test", "http://keep-beta.test", "Mixed"));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, mixed.StatusCode);

        var requests = await client.GetFromJsonAsync<List<RegisteredServerDto>>("/api/hub/join-requests", JsonOptions);
        Assert.NotNull(requests);
        Assert.Contains(requests, s => s.ServerDid == "did:web:keep-alpha.test" && s.BaseUrl == "http://keep-alpha.test");
        Assert.Contains(requests, s => s.ServerDid == "did:web:keep-beta.test" && s.BaseUrl == "http://keep-beta.test");
    }

    [Fact]
    public async Task SamePartyCannotAcceptOwnRequest()
    {
        using var client = await Authed();
        var did = "did:web:own-accept.test";
        (await client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest(did, "http://own-accept.test", "Own Accept")))
            .EnsureSuccessStatusCode();

        var accept = await client.PostAsJsonAsync(
            "/api/hub/join-requests/accept",
            new AcceptJoinRequest(did, "hub"));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, accept.StatusCode);
    }

    [Fact]
    public async Task Decline_RemovesPendingRequest()
    {
        using var client = await Authed();
        var did = "did:web:decline.test";
        (await client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest(did, "http://decline.test", "Decline Test")))
            .EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync(
            "/api/hub/join-requests/decline",
            new DeclineJoinRequest(did))).EnsureSuccessStatusCode();

        var requests = await client.GetFromJsonAsync<List<RegisteredServerDto>>(
            $"/api/hub/join-requests?serverDid={Uri.EscapeDataString(did)}", JsonOptions);
        Assert.NotNull(requests);
        Assert.DoesNotContain(requests, s => s.ServerDid == did);
    }

    [Fact]
    public async Task UnregisterServer_RemovesFromMap()
    {
        using var client = await Authed();
        var did = "did:web:leave.test";
        var register = await client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest(did, "http://leave.test", "Leave Test"));
        register.EnsureSuccessStatusCode();
        var pending = await register.Content.ReadFromJsonAsync<RegisteredServerDto>(JsonOptions);
        (await client.PostAsJsonAsync(
            "/api/hub/join-requests/accept",
            new AcceptJoinRequest(did, "origin", pending!.OriginInviteId))).EnsureSuccessStatusCode();

        var delete = await client.DeleteAsync($"/api/hub/servers/{Uri.EscapeDataString(did)}");
        delete.EnsureSuccessStatusCode();

        var map = await client.GetFromJsonAsync<FederationMapDto>("/api/hub/map", JsonOptions);
        Assert.NotNull(map);
        Assert.DoesNotContain(map.Servers, s => s.ServerDid == did);
    }

    [Fact]
    public async Task UnregisterServer_QueryString_RemovesPercentEncodedDid()
    {
        using var client = await Authed();
        var did = "did:web:localhost%3A5001";
        var register = await client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest(did, "http://localhost:5001", "Snekl Alpha"));
        register.EnsureSuccessStatusCode();
        var pending = await register.Content.ReadFromJsonAsync<RegisteredServerDto>(JsonOptions);
        (await client.PostAsJsonAsync(
            "/api/hub/join-requests/accept",
            new AcceptJoinRequest(did, "origin", pending!.OriginInviteId))).EnsureSuccessStatusCode();

        var delete = await client.DeleteAsync(
            $"/api/hub/servers?serverDid={Uri.EscapeDataString(did)}");
        delete.EnsureSuccessStatusCode();

        var map = await client.GetFromJsonAsync<FederationMapDto>("/api/hub/map", JsonOptions);
        Assert.NotNull(map);
        Assert.DoesNotContain(map.Servers, s => s.ServerDid == did);
    }

    [Fact]
    public async Task Feed_IngestsPublicCard_HidesMembersAndTombstones()
    {
        using var client = await Authed();
        var did = $"did:web:feed-{Guid.NewGuid():N}.test";
        var register = await client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest(did, $"http://{did["did:web:".Length..]}", "Feed Origin"));
        register.EnsureSuccessStatusCode();
        var pending = await register.Content.ReadFromJsonAsync<RegisteredServerDto>(JsonOptions);
        (await client.PostAsJsonAsync(
            "/api/hub/join-requests/accept",
            new AcceptJoinRequest(did, "origin", pending!.OriginInviteId))).EnsureSuccessStatusCode();

        var publicId = Guid.NewGuid();
        var membersId = Guid.NewGuid();
        var created = DateTime.UtcNow.AddHours(-3);
        var publicCard = FeedCard(did, publicId, "public", created, score: 8);
        var membersCard = FeedCard(did, membersId, "members", created, score: 99);

        var unknown = await client.PostAsJsonAsync("/api/hub/cards", FeedCard("did:web:not-registered.test", Guid.NewGuid(), "public", created));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, unknown.StatusCode);

        (await client.PostAsJsonAsync("/api/hub/cards", publicCard)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/hub/cards", membersCard)).EnsureSuccessStatusCode();

        var feed = await client.GetFromJsonAsync<List<HubFeedCardDto>>("/api/hub/feed?sort=hot&limit=50", JsonOptions);
        Assert.NotNull(feed);
        Assert.Contains(feed, c => c.PostId == publicId && c.Score == 8);
        Assert.DoesNotContain(feed, c => c.PostId == membersId);

        var tombstone = publicCard with { Event = "delete", EventAt = DateTime.UtcNow };
        (await client.PostAsJsonAsync("/api/hub/cards", tombstone)).EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<List<HubFeedCardDto>>("/api/hub/feed", JsonOptions);
        Assert.NotNull(after);
        Assert.DoesNotContain(after, c => c.PostId == publicId);
    }

    [Fact]
    public async Task Search_FindsTitleAcrossOrigins_HidesUnrelatedAndMembers()
    {
        using var client = await Authed();
        var did = $"did:web:search-{Guid.NewGuid():N}.test";
        var register = await client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest(did, $"http://{did["did:web:".Length..]}", "Search Origin"));
        register.EnsureSuccessStatusCode();
        var pending = await register.Content.ReadFromJsonAsync<RegisteredServerDto>(JsonOptions);
        (await client.PostAsJsonAsync(
            "/api/hub/join-requests/accept",
            new AcceptJoinRequest(did, "origin", pending!.OriginInviteId))).EnsureSuccessStatusCode();

        var hitId = Guid.NewGuid();
        var missId = Guid.NewGuid();
        var membersId = Guid.NewGuid();
        var created = DateTime.UtcNow.AddHours(-1);
        (await client.PostAsJsonAsync("/api/hub/cards", FeedCard(did, hitId, "public", created, title: "Pineapple pizza", excerpt: "Dinner debate"))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/hub/cards", FeedCard(did, missId, "public", created, title: "Tomatoes", excerpt: "Gardening notes"))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/hub/cards", FeedCard(did, membersId, "members", created, title: "Pineapple secret", excerpt: "Members only"))).EnsureSuccessStatusCode();

        var hits = await client.GetFromJsonAsync<List<HubSearchHitDto>>("/api/hub/search?q=pineapple", JsonOptions);
        Assert.NotNull(hits);
        Assert.Contains(hits, h => h.Thread.PostId == hitId);
        Assert.DoesNotContain(hits, h => h.Thread.PostId == missId);
        Assert.DoesNotContain(hits, h => h.Thread.PostId == membersId);

        var empty = await client.GetFromJsonAsync<List<HubSearchHitDto>>("/api/hub/search?q=", JsonOptions);
        Assert.NotNull(empty);
        Assert.Empty(empty);

        var page = await client.GetFromJsonAsync<List<HubSearchHitDto>>("/api/hub/search?q=pineapple&limit=1&offset=0", JsonOptions);
        Assert.NotNull(page);
        Assert.True(page.Count <= 1);
    }

    [Fact]
    public async Task Feed_OffsetReturnsLaterCards()
    {
        using var client = await Authed();
        var did = $"did:web:offset-{Guid.NewGuid():N}.test";
        var register = await client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest(did, $"http://{did["did:web:".Length..]}", "Offset Origin"));
        register.EnsureSuccessStatusCode();
        var pending = await register.Content.ReadFromJsonAsync<RegisteredServerDto>(JsonOptions);
        (await client.PostAsJsonAsync(
            "/api/hub/join-requests/accept",
            new AcceptJoinRequest(did, "origin", pending!.OriginInviteId))).EnsureSuccessStatusCode();

        var stamp = DateTime.UtcNow.AddYears(80);
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();
        for (var i = 0; i < ids.Count; i++)
        {
            var card = FeedCard(did, ids[i], "public", stamp.AddSeconds(i), score: 1);
            (await client.PostAsJsonAsync("/api/hub/cards", card)).EnsureSuccessStatusCode();
        }

        var first = await client.GetFromJsonAsync<List<HubFeedCardDto>>("/api/hub/feed?sort=new&limit=2", JsonOptions);
        var second = await client.GetFromJsonAsync<List<HubFeedCardDto>>("/api/hub/feed?sort=new&limit=2&offset=2", JsonOptions);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(2, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Empty(first.Select(c => c.PostId).Intersect(second.Select(c => c.PostId)));
        Assert.Contains(ids[3], first.Select(c => c.PostId));
        Assert.Contains(ids[2], first.Select(c => c.PostId));
        Assert.Contains(ids[1], second.Select(c => c.PostId));
        Assert.Contains(ids[0], second.Select(c => c.PostId));
    }

    [Fact]
    public async Task Feed_MemberJwtIncludesJoinedMembersCards()
    {
        using var client = await Authed();
        var did = $"did:web:members-{Guid.NewGuid():N}.test";
        var register = await client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest(did, $"http://{did["did:web:".Length..]}", "Members Origin"));
        register.EnsureSuccessStatusCode();
        var pending = await register.Content.ReadFromJsonAsync<RegisteredServerDto>(JsonOptions);
        (await client.PostAsJsonAsync(
            "/api/hub/join-requests/accept",
            new AcceptJoinRequest(did, "origin", pending!.OriginInviteId))).EnsureSuccessStatusCode();

        var forumId = Guid.NewGuid();
        var membersId = Guid.NewGuid();
        var created = DateTime.UtcNow.AddHours(-2);
        var membersCard = FeedCard(did, membersId, "members", created, score: 40, forumId: forumId);
        (await client.PostAsJsonAsync("/api/hub/cards", membersCard)).EnsureSuccessStatusCode();

        var anonymous = await _fixture.Client.GetFromJsonAsync<List<HubFeedCardDto>>("/api/hub/feed", JsonOptions);
        Assert.DoesNotContain(anonymous ?? [], c => c.PostId == membersId);

        var userDid = "did:key:joiner";
        var unknown = await client.PostAsJsonAsync(
            "/api/hub/memberships",
            Membership(userDid, "did:web:not-registered.test", forumId, "upsert"));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, unknown.StatusCode);

        (await client.PostAsJsonAsync(
            "/api/hub/memberships",
            Membership(userDid, did, forumId, "upsert"))).EnsureSuccessStatusCode();

        using var member = _fixture.CreateMemberClient(userDid, "joiner");
        var withMembership = await member.GetFromJsonAsync<List<HubFeedCardDto>>("/api/hub/feed?sort=hot&limit=50", JsonOptions);
        Assert.Contains(withMembership ?? [], c => c.PostId == membersId);

        (await client.PostAsJsonAsync(
            "/api/hub/memberships",
            Membership(userDid, did, forumId, "delete"))).EnsureSuccessStatusCode();

        var afterLeave = await member.GetFromJsonAsync<List<HubFeedCardDto>>("/api/hub/feed", JsonOptions);
        Assert.DoesNotContain(afterLeave ?? [], c => c.PostId == membersId);
    }

    [Fact]
    public async Task Feed_StartupPullIngestsOriginRecents()
    {
        using var client = await Authed();
        var did = $"did:web:pull-{Guid.NewGuid():N}.test";
        var baseUrl = $"http://{did["did:web:".Length..]}";
        var register = await client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest(did, baseUrl, "Pull Origin"));
        register.EnsureSuccessStatusCode();
        var pending = await register.Content.ReadFromJsonAsync<RegisteredServerDto>(JsonOptions);
        (await client.PostAsJsonAsync(
            "/api/hub/join-requests/accept",
            new AcceptJoinRequest(did, "origin", pending!.OriginInviteId))).EnsureSuccessStatusCode();

        var postId = Guid.NewGuid();
        FakeOriginRecentClient.Threads[baseUrl] =
        [
            new OriginRecentThread
            {
                ForumSlug = "garden",
                ForumName = "Garden",
                OwnerBaseUrl = baseUrl,
                OwnerDid = did,
                Post = new OriginRecentPost
                {
                    Id = postId,
                    ForumId = Guid.NewGuid(),
                    Title = "Existing thread",
                    Message = "Already on the origin",
                    UpvoteCount = 6,
                    ReplyCount = 2,
                    CreatedAt = DateTime.UtcNow.AddHours(-4)
                }
            }
        ];

        using var scope = _fixture.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IHubFeedOriginSync>().SyncAsync();

        var feed = await client.GetFromJsonAsync<List<HubFeedCardDto>>("/api/hub/feed?sort=hot&limit=50", JsonOptions);
        Assert.NotNull(feed);
        Assert.Contains(feed, c => c.PostId == postId && c.Score == 6 && c.Title == "Existing thread");
    }

    [Fact]
    public async Task RefreshFeed_UnauthorizedWithoutOperatorJwt()
    {
        var anon = await _fixture.Client.PostAsync("/api/hub/admin/feed/sync", null);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, anon.StatusCode);

        using var member = _fixture.CreateMemberClient();
        var memberPost = await member.PostAsync("/api/hub/admin/feed/sync", null);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, memberPost.StatusCode);
    }

    [Fact]
    public async Task RefreshFeed_OperatorPullsOriginRecents()
    {
        using var client = await Authed();
        var did = $"did:web:admin-pull-{Guid.NewGuid():N}.test";
        var baseUrl = $"http://{did["did:web:".Length..]}";
        var register = await client.PostAsJsonAsync(
            "/api/hub/servers",
            new RegisterServerRequest(did, baseUrl, "Admin Pull Origin"));
        register.EnsureSuccessStatusCode();
        var pending = await register.Content.ReadFromJsonAsync<RegisteredServerDto>(JsonOptions);
        (await client.PostAsJsonAsync(
            "/api/hub/join-requests/accept",
            new AcceptJoinRequest(did, "origin", pending!.OriginInviteId))).EnsureSuccessStatusCode();

        var postId = Guid.NewGuid();
        FakeOriginRecentClient.Threads[baseUrl] =
        [
            new OriginRecentThread
            {
                ForumSlug = "garden",
                ForumName = "Garden",
                OwnerBaseUrl = baseUrl,
                OwnerDid = did,
                Post = new OriginRecentPost
                {
                    Id = postId,
                    ForumId = Guid.NewGuid(),
                    Title = "Admin pull",
                    Message = "From the operator button",
                    UpvoteCount = 3,
                    CreatedAt = DateTime.UtcNow.AddHours(-1)
                }
            }
        ];

        var sync = await client.PostAsync("/api/hub/admin/feed/sync", null);
        sync.EnsureSuccessStatusCode();
        var result = await sync.Content.ReadFromJsonAsync<HubFeedSyncResultDto>(JsonOptions);
        Assert.NotNull(result);
        Assert.True(result.OriginCount >= 1);
        Assert.Contains(result.Origins, o => o.ServerDid == did && o.Ingested == 1 && o.Error is null);

        var feed = await client.GetFromJsonAsync<List<HubFeedCardDto>>("/api/hub/feed?sort=hot&limit=50", JsonOptions);
        Assert.NotNull(feed);
        Assert.Contains(feed, c => c.PostId == postId && c.Title == "Admin pull");
    }

    private static HubFeedCardDto FeedCard(
        string serverDid,
        Guid postId,
        string visibility,
        DateTime created,
        int score = 1,
        Guid? forumId = null,
        string title = "Hello",
        string excerpt = "Excerpt") =>
        new(
            1,
            postId,
            forumId ?? Guid.NewGuid(),
            "garden",
            "Garden",
            serverDid,
            "http://origin.test",
            $"http://origin.test/f/garden/{postId:D}",
            title,
            excerpt,
            null,
            "did:key:alice",
            "Alice",
            created,
            null,
            visibility,
            score,
            0,
            1,
            "upsert",
            created);

    private static HubForumMembershipDto Membership(string userDid, string serverDid, Guid forumId, string evt) =>
        new(1, userDid, serverDid, forumId, evt, DateTime.UtcNow);
}
