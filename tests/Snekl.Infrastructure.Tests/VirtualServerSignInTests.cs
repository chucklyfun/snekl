using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Snekl.Domain.Entities;
using Snekl.Domain.Identity;
using Snekl.Domain.Identity.Did;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;
using Snekl.Redis;

namespace Snekl.Infrastructure.Tests;

/// <summary>
/// Spawned servers inherit the host's auth issuers and hubs live, keep a required business
/// issuer no matter what, and sign-in resolves trust and people inside the requested server.
/// </summary>
public class VirtualServerSignInTests : IDisposable
{
    private const string HostAuth = "http://localhost:5100";
    private const string BetaAuth = "http://localhost:5101";
    private const string GammaAuth = "http://gamma-auth.test";
    private const string OtherAuth = "http://other-auth.test";
    private const string HostHub = "http://hub-a.test";
    private const string LaterHub = "http://hub-b.test";
    private const string OwnHub = "http://own-hub.test";
    private const string OwnerDid = "did:key:zOwnerSignIn";

    private static readonly Guid SubjectId = Guid.Parse("5c2b9c1e-0000-4000-8000-000000000001");

    private readonly string _templatePath = Path.Combine(Path.GetTempPath(), $"snekl-template-{Guid.NewGuid():N}.json");
    private readonly string _dbName = Guid.NewGuid().ToString();

    public void Dispose()
    {
        if (File.Exists(_templatePath))
            File.Delete(_templatePath);
    }

    [Fact]
    public async Task Spawned_server_reads_host_issuers_live_without_copying_them()
    {
        var host = await SeedHostAsync();
        var spawned = await SpawnAsync(new ServerTemplateDocument());

        Assert.Empty(await OwnIssuersAsync(spawned.Id));
        Assert.Equal(new[] { HostAuth, BetaAuth }, await EffectiveUrlsAsync(spawned.Id));
        // The host only sees its own active rows.
        Assert.Equal(new[] { HostAuth, BetaAuth }, await EffectiveUrlsAsync(host));
    }

    [Fact]
    public async Task Host_issuer_added_later_reaches_an_existing_spawned_server()
    {
        var host = await SeedHostAsync();
        var spawned = await SpawnAsync(new ServerTemplateDocument());
        Assert.False(CreateAuth(spawned.Id).IsTrustedIssuer(GammaAuth));

        await AddHostIssuerAsync(host, "Gamma", GammaAuth);

        Assert.Contains(GammaAuth, await EffectiveUrlsAsync(spawned.Id));
        Assert.True(CreateAuth(spawned.Id).IsTrustedIssuer(GammaAuth));
    }

    [Fact]
    public async Task Host_issuer_removed_is_dropped_unless_it_is_the_required_business_issuer()
    {
        var host = await SeedHostAsync();
        var business = await SpawnAsync(new ServerTemplateDocument(), requiredIssuer: HostAuth + "/");
        var plain = await SpawnAsync(new ServerTemplateDocument());

        var pinned = Assert.Single(await OwnIssuersAsync(business.Id));
        Assert.True(pinned.IsRequired);
        Assert.Equal(HostAuth, pinned.IssuerUrl);
        Assert.Equal($"{HostAuth}/.well-known/jwks.json", pinned.JwksUrl);

        await using (var db = CreateDb(new CurrentServer()))
        {
            foreach (var row in await db.TrustedAuthIssuers.IgnoreQueryFilters().Where(i => i.ServerId == host && i.IsActive).ToListAsync())
                row.IsActive = false;
            await db.SaveChangesAsync();
        }

        Assert.Equal(new[] { HostAuth }, await EffectiveUrlsAsync(business.Id));
        Assert.True(CreateAuth(business.Id).IsTrustedIssuer(HostAuth));
        Assert.False(CreateAuth(business.Id).IsTrustedIssuer(BetaAuth));
        Assert.Empty(await EffectiveUrlsAsync(plain.Id));
        Assert.False(CreateAuth(plain.Id).IsTrustedIssuer(HostAuth));

        // Even with the host's row gone entirely, the business server keeps its issuer.
        await using (var db = CreateDb(new CurrentServer()))
        {
            db.TrustedAuthIssuers.RemoveRange(await db.TrustedAuthIssuers.IgnoreQueryFilters().Where(i => i.ServerId == host).ToListAsync());
            await db.SaveChangesAsync();
        }

        Assert.True(CreateAuth(business.Id).IsTrustedIssuer(HostAuth));
    }

    [Fact]
    public async Task Required_business_issuer_cannot_be_removed_disabled_unpinned_or_repointed()
    {
        await SeedHostAsync();
        var business = await SpawnAsync(new ServerTemplateDocument(), requiredIssuer: HostAuth);

        await AssertRefusedAsync(business.Id, row => row.IsActive = false);
        await AssertRefusedAsync(business.Id, row => row.IsRequired = false);
        await AssertRefusedAsync(business.Id, row => row.JwksUrl = "http://attacker.test/jwks");
        await AssertRefusedAsync(business.Id, row => row.IssuerUrl = OtherAuth);
        await AssertRefusedAsync(business.Id, row => { }, delete: true);

        // Renaming is fine.
        await using (var db = CreateDb(new CurrentServer()))
        {
            var row = await db.TrustedAuthIssuers.IgnoreQueryFilters().SingleAsync(i => i.ServerId == business.Id);
            row.Name = "Business auth";
            await db.SaveChangesAsync();
        }

        var stored = Assert.Single(await OwnIssuersAsync(business.Id));
        Assert.Equal("Business auth", stored.Name);
        Assert.True(stored.IsActive && stored.IsRequired);
    }

    [Fact]
    public async Task Admin_register_cannot_repoint_the_required_issuer_but_can_rename_it()
    {
        await SeedHostAsync();
        var business = await SpawnAsync(new ServerTemplateDocument(), requiredIssuer: HostAuth);
        var current = new CurrentServer();
        current.Use(business.Id);

        await using (var db = CreateDb(current))
        {
            var mapping = new PersonClusterMappingService(db);
            var error = await Assert.ThrowsAsync<RequiredAuthIssuerException>(() =>
                mapping.RegisterTrustedIssuerAsync(new RegisterTrustedIssuerRequest(HostAuth, "http://attacker.test/jwks", "Evil")));
            Assert.Equal(HostAuth, error.IssuerUrl);
            Assert.Contains("required by this server's business account", error.Message);
        }

        await using (var db = CreateDb(current))
        {
            var mapping = new PersonClusterMappingService(db);
            var renamed = await mapping.RegisterTrustedIssuerAsync(
                new RegisterTrustedIssuerRequest(HostAuth, $"{HostAuth}/.well-known/jwks.json", "Renamed"));
            Assert.True(renamed.IsRequired);
            Assert.Contains((await mapping.ListTrustedIssuersAsync()), i => i.Name == "Renamed" && i.IsRequired);
        }
    }

    [Fact]
    public async Task Inheritance_off_uses_only_the_servers_own_issuers_and_hubs()
    {
        await SeedHostAsync();
        var spawned = await SpawnAsync(new ServerTemplateDocument
        {
            InheritHostAuthIssuers = false,
            InheritHostHubs = false,
            AuthIssuers = [new ServerTemplateIssuer { Name = "Other", IssuerUrl = OtherAuth, JwksUrl = $"{OtherAuth}/jwks" }],
            Hub = new ServerTemplateHub { Name = "Own", BaseUrl = OwnHub }
        });

        await using (var db = CreateDb(new CurrentServer()))
        {
            var node = await db.NodeConfig.SingleAsync(n => n.Id == spawned.Id);
            Assert.False(node.InheritHostAuthIssuers);
            Assert.False(node.InheritHostHubs);
        }

        Assert.Equal(new[] { OtherAuth }, await EffectiveUrlsAsync(spawned.Id));
        Assert.Equal(new[] { OwnHub }, await EffectiveHubsAsync(spawned.Id));
        Assert.False(CreateAuth(spawned.Id).IsTrustedIssuer(HostAuth));
        Assert.True(CreateAuth(spawned.Id).IsTrustedIssuer(OtherAuth));

        // Inheritance off but a required issuer still counts.
        var pinned = await SpawnAsync(new ServerTemplateDocument { InheritHostAuthIssuers = false }, requiredIssuer: BetaAuth);
        Assert.Equal(new[] { BetaAuth }, await EffectiveUrlsAsync(pinned.Id));
    }

    [Fact]
    public async Task Host_hub_added_later_reaches_an_existing_spawned_server()
    {
        var host = await SeedHostAsync();
        var spawned = await SpawnAsync(new ServerTemplateDocument());
        Assert.Equal(new[] { HostHub }, await EffectiveHubsAsync(spawned.Id));
        Assert.Equal(HostHub, (await NodeInfoAsync(spawned.Id)).HubBaseUrl);

        await AddHubAsync(host, LaterHub, "accepted", DateTime.UtcNow.AddMinutes(1));
        await AddHubAsync(host, "http://pending-hub.test", "pending", DateTime.UtcNow.AddMinutes(2));

        Assert.Equal(new[] { LaterHub, HostHub }, await EffectiveHubsAsync(spawned.Id));
        Assert.Equal(LaterHub, (await NodeInfoAsync(spawned.Id)).HubBaseUrl);

        // A host hub that is no longer accepted is dropped.
        await using (var db = CreateDb(new CurrentServer()))
        {
            var later = await db.HubInvites.IgnoreQueryFilters().SingleAsync(i => i.HubBaseUrl == LaterHub);
            later.Status = "declined";
            await db.SaveChangesAsync();
        }

        Assert.Equal(new[] { HostHub }, await EffectiveHubsAsync(spawned.Id));
    }

    [Fact]
    public async Task Spawned_servers_own_hub_comes_first_and_duplicates_collapse()
    {
        await SeedHostAsync();
        var spawned = await SpawnAsync(new ServerTemplateDocument { Hub = new ServerTemplateHub { Name = "Own", BaseUrl = OwnHub } });
        var same = await SpawnAsync(new ServerTemplateDocument { Hub = new ServerTemplateHub { Name = "Host hub", BaseUrl = HostHub + "/" } });

        Assert.Equal(new[] { OwnHub, HostHub }, await EffectiveHubsAsync(spawned.Id));
        Assert.Equal(OwnHub, (await NodeInfoAsync(spawned.Id)).HubBaseUrl);
        Assert.Equal(new[] { HostHub }, await EffectiveHubsAsync(same.Id));
    }

    [Fact]
    public async Task Requiring_an_issuer_the_host_does_not_trust_fails_and_leaves_nothing_behind()
    {
        await SeedHostAsync();
        var store = new ServerTemplateStore(_templatePath);
        store.Write(new ServerTemplateDocument());
        await using var db = CreateDb(new CurrentServer());
        var service = new VirtualServerService(db, store, Config());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SpawnAsync(new SpawnServerRequest("Nope", "owner", OwnerDid, OtherAuth)));
        Assert.Contains("does not trust", error.Message);

        await using var check = CreateDb(new CurrentServer());
        Assert.Equal(1, await check.NodeConfig.CountAsync());
    }

    [Fact]
    public async Task Pinning_later_marks_an_existing_server_issuer_as_required()
    {
        await SeedHostAsync();
        var spawned = await SpawnAsync(new ServerTemplateDocument());
        await using (var db = CreateDb(new CurrentServer()))
        {
            var service = new VirtualServerService(db, new ServerTemplateStore(_templatePath), Config());
            await service.PinAuthIssuerAsync(spawned.Id, BetaAuth);
            await service.PinAuthIssuerAsync(spawned.Id, BetaAuth);
        }

        var pinned = Assert.Single(await OwnIssuersAsync(spawned.Id));
        Assert.Equal(BetaAuth, pinned.IssuerUrl);
        Assert.True(pinned.IsRequired);
    }

    [Fact]
    public async Task Repo_template_inherits_host_issuers_and_hubs()
    {
        var repoTemplate = FindRepoFile(Path.Combine("src", "Snekl.Api", "server-template.json"));
        File.Copy(repoTemplate, _templatePath, overwrite: true);
        var document = new ServerTemplateStore(_templatePath).Read();
        Assert.True(document.InheritHostAuthIssuers);
        Assert.True(document.InheritHostHubs);

        await SeedHostAsync();
        await using var db = CreateDb(new CurrentServer());
        var service = new VirtualServerService(db, new ServerTemplateStore(_templatePath), Config());
        var spawned = await service.SpawnAsync(new SpawnServerRequest("Repo Template", "owner", OwnerDid));

        Assert.Contains(HostAuth, await EffectiveUrlsAsync(spawned.Id));
        Assert.Equal(new[] { HostHub }, await EffectiveHubsAsync(spawned.Id));
    }

    [Fact]
    public void Template_without_the_flags_defaults_to_inheriting()
    {
        File.WriteAllText(_templatePath, """{ "authIssuers": [], "hub": { "name": "", "baseUrl": "" }, "roles": [] }""");
        var document = new ServerTemplateStore(_templatePath).Read();
        Assert.True(document.InheritHostAuthIssuers);
        Assert.True(document.InheritHostHubs);
        Assert.True(ServerTemplateStore.Default().InheritHostHubs);
    }

    [Fact]
    public async Task Owner_sign_in_resolves_to_the_spawned_owner_even_with_other_shadow_users()
    {
        var host = await SeedHostAsync();
        await AddUserAsync(host, "owner", OwnerDid, HostAuth);
        await AddUserAsync(Guid.Empty, "owner", OwnerDid, HostAuth);
        var spawned = await SpawnAsync(new ServerTemplateDocument());

        var user = await CreateAuth(spawned.Id, out var cache).GetOrCreateShadowUserAsync(Principal(HostAuth));

        Assert.Equal(spawned.OwnerUserId, user.Id);
        Assert.Equal(spawned.Id, user.ServerId);
        await using var db = CreateDb(new CurrentServer());
        Assert.Equal(3, await db.Users.IgnoreQueryFilters().CountAsync(u => u.Did == OwnerDid));
        Assert.Null(await cache.GetStringAsync(Snekl.Infrastructure.Caching.CacheKeys.ShadowUser(HostAuth, SubjectId)));
    }

    [Fact]
    public async Task Sign_in_adopts_a_legacy_unscoped_shadow_user_instead_of_creating_a_new_one()
    {
        var host = await SeedHostAsync();
        var legacy = await AddUserAsync(Guid.Empty, "owner", OwnerDid, HostAuth);

        var user = await CreateAuth(host).GetOrCreateShadowUserAsync(Principal(HostAuth));

        Assert.Equal(legacy, user.Id);
        await using var db = CreateDb(new CurrentServer());
        Assert.Equal(host, (await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Did == OwnerDid)).ServerId);
    }

    [Fact]
    public async Task Request_scope_follows_the_server_header_like_the_middleware()
    {
        var host = await SeedHostAsync();
        await using var db = CreateDb(new CurrentServer());

        Assert.Equal(host, RequestServerScope.Resolve(new DefaultHttpContext(), db));

        var spawned = await SpawnAsync(new ServerTemplateDocument());
        Assert.Null(RequestServerScope.Resolve(new DefaultHttpContext(), db));
        Assert.Equal(spawned.Id, RequestServerScope.Resolve(WithHeader(spawned.Id.ToString()), db));
        Assert.Equal(host, RequestServerScope.Resolve(WithHeader(host.ToString()), db));
        Assert.Null(RequestServerScope.Resolve(WithHeader("not-a-guid"), db));
        Assert.Null(RequestServerScope.Resolve(WithHeader(Guid.NewGuid().ToString()), db));
        Assert.Null(RequestServerScope.Resolve(null, db));

        var node = await db.NodeConfig.SingleAsync(n => n.Id == spawned.Id);
        node.DisabledAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Assert.Null(RequestServerScope.Resolve(WithHeader(spawned.Id.ToString()), db));
        Assert.Equal(host, RequestServerScope.Resolve(new DefaultHttpContext(), db));
    }

    [Fact]
    public async Task Hub_outbox_messages_are_signed_as_the_server_that_queued_them()
    {
        var host = await SeedHostAsync();
        var spawned = await SpawnAsync(new ServerTemplateDocument());
        await using (var db = CreateDb(new CurrentServer()))
        {
            db.HubOutbox.AddRange(
                Outbox(host, "/api/hub/cards"),
                Outbox(spawned.Id, "/api/hub/cards"),
                Outbox(Guid.Empty, "/api/hub/memberships"));
            await db.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddScoped<ICurrentServer, CurrentServer>();
        services.AddScoped(sp => CreateDb(sp.GetRequiredService<ICurrentServer>()));
        services.AddSingleton(Config());
        services.AddSingleton(Options.Create(new RateLimitOptions()));
        services.AddScoped<INodeService, NodeService>();
        await using var provider = services.BuildServiceProvider();
        var handler = new CapturingHandler();
        var dispatcher = new HubOutboxDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new StubHttpClientFactory(handler),
            NullLogger<HubOutboxDispatcher>.Instance);

        var drain = typeof(HubOutboxDispatcher).GetMethod("DrainOnceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)drain.Invoke(dispatcher, [CancellationToken.None])!;

        Assert.Equal(3, handler.Bodies.Count);
        var senders = handler.Bodies
            .Select(b => JsonDocument.Parse(b).RootElement)
            .Select(e => Guid.Parse(e.EnumerateObject().First(p => p.Name.Equals("sourceNodeId", StringComparison.OrdinalIgnoreCase)).Value.GetString()!))
            .ToList();
        Assert.Equal(2, senders.Count(id => id == host));
        Assert.Equal(1, senders.Count(id => id == spawned.Id));
    }

    private static HubOutboxMessage Outbox(Guid serverId, string path) => new()
    {
        ServerId = serverId,
        HubBaseUrl = HostHub,
        Path = path,
        EnvelopeType = "hub.test",
        PayloadJson = "{}",
        NextAttemptAt = DateTime.UtcNow.AddMinutes(-1)
    };

    private async Task AssertRefusedAsync(Guid serverId, Action<TrustedAuthIssuer> change, bool delete = false)
    {
        await using var db = CreateDb(new CurrentServer());
        var row = await db.TrustedAuthIssuers.IgnoreQueryFilters().SingleAsync(i => i.ServerId == serverId && i.IsRequired);
        if (delete)
            db.TrustedAuthIssuers.Remove(row);
        else
            change(row);
        var error = await Assert.ThrowsAsync<RequiredAuthIssuerException>(() => db.SaveChangesAsync());
        Assert.Equal(HostAuth, error.IssuerUrl);
    }

    private static DefaultHttpContext WithHeader(string value)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers[RequestServerScope.HeaderName] = value;
        return http;
    }

    private async Task<Guid> SeedHostAsync()
    {
        await using var db = CreateDb(new CurrentServer());
        var keys = DidKey.Generate();
        var host = new NodeConfig
        {
            Name = "Host",
            BaseUrl = "http://localhost:5001",
            Did = "did:web:localhost%3A5001",
            VerificationKeyMultibase = DidKey.PublicKeyMultibase(keys.Did),
            VerificationPrivateKeyPkcs8 = Convert.ToBase64String(keys.PrivateKey),
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        db.NodeConfig.Add(host);
        db.TrustedAuthIssuers.AddRange(
            new TrustedAuthIssuer { ServerId = host.Id, Name = "Alpha", IssuerUrl = HostAuth, JwksUrl = $"{HostAuth}/.well-known/jwks.json", IsActive = true },
            new TrustedAuthIssuer { ServerId = host.Id, Name = "Beta", IssuerUrl = BetaAuth, JwksUrl = $"{BetaAuth}/.well-known/jwks.json", IsActive = true },
            new TrustedAuthIssuer { ServerId = host.Id, Name = "Retired", IssuerUrl = "http://retired.test", JwksUrl = "http://retired.test/jwks", IsActive = false });
        db.HubInvites.Add(new HubInvite
        {
            ServerId = host.Id,
            HubBaseUrl = HostHub,
            HubName = "Hub A",
            RequestedByDid = host.Did,
            RequestedByUsername = "host",
            Status = "accepted",
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();
        return host.Id;
    }

    private async Task AddHostIssuerAsync(Guid host, string name, string url)
    {
        await using var db = CreateDb(new CurrentServer());
        db.TrustedAuthIssuers.Add(new TrustedAuthIssuer { ServerId = host, Name = name, IssuerUrl = url, JwksUrl = $"{url}/jwks", IsActive = true });
        await db.SaveChangesAsync();
    }

    private async Task AddHubAsync(Guid serverId, string url, string status, DateTime createdAt)
    {
        await using var db = CreateDb(new CurrentServer());
        db.HubInvites.Add(new HubInvite
        {
            ServerId = serverId,
            HubBaseUrl = url,
            HubName = url,
            RequestedByDid = "did:web:host",
            RequestedByUsername = "host",
            Status = status,
            CreatedAt = createdAt
        });
        await db.SaveChangesAsync();
    }

    private async Task<VirtualServerSummary> SpawnAsync(ServerTemplateDocument template, string? requiredIssuer = null)
    {
        var store = new ServerTemplateStore(_templatePath);
        store.Write(template);
        await using var db = CreateDb(new CurrentServer());
        var service = new VirtualServerService(db, store, Config());
        return await service.SpawnAsync(new SpawnServerRequest(
            $"Spawned {Guid.NewGuid():N}"[..16], "owner", OwnerDid, requiredIssuer));
    }

    private async Task<List<TrustedAuthIssuer>> OwnIssuersAsync(Guid serverId)
    {
        await using var db = CreateDb(new CurrentServer());
        return await db.TrustedAuthIssuers.IgnoreQueryFilters().AsNoTracking().Where(i => i.ServerId == serverId).ToListAsync();
    }

    private async Task<string[]> EffectiveUrlsAsync(Guid serverId)
    {
        var current = new CurrentServer();
        current.Use(serverId);
        await using var db = CreateDb(current);
        return (await db.EffectiveTrustedIssuersAsync()).Select(i => i.IssuerUrl).Order(StringComparer.Ordinal).ToArray();
    }

    private async Task<string[]> EffectiveHubsAsync(Guid serverId)
    {
        var current = new CurrentServer();
        current.Use(serverId);
        await using var db = CreateDb(current);
        return (await db.EffectiveAcceptedHubUrlsAsync()).ToArray();
    }

    private async Task<Snekl.Domain.Dtos.NodeInfoDto> NodeInfoAsync(Guid serverId)
    {
        var current = new CurrentServer();
        current.Use(serverId);
        await using var db = CreateDb(current);
        return await new NodeService(db, Options.Create(new RateLimitOptions()), Config(), current).GetInfoAsync();
    }

    private async Task<Guid> AddUserAsync(Guid serverId, string username, string did, string issuer)
    {
        await using var db = CreateDb(new CurrentServer());
        var user = new User
        {
            ServerId = serverId,
            Username = username,
            DisplayName = username,
            Email = $"{username}@example.com",
            Bio = "",
            Did = did,
            AuthIssuerUrl = issuer,
            AuthSubjectId = SubjectId,
            PasswordHash = "x"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static ClaimsPrincipal Principal(string issuer) => new(new ClaimsIdentity(
    [
        new Claim("did", OwnerDid),
        new Claim("iss", issuer),
        new Claim("sub", SubjectId.ToString()),
        new Claim("username", "owner"),
        new Claim("email", "owner@example.com")
    ], "test"));

    private ExternalAuthService CreateAuth(Guid serverId) => CreateAuth(serverId, out _);

    private ExternalAuthService CreateAuth(Guid serverId, out IDistributedCache cache)
    {
        var current = new CurrentServer();
        current.Use(serverId);
        cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        return new ExternalAuthService(CreateDb(current), null!, null!, Config(), cache);
    }

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Node:BaseUrl"] = "http://localhost:5001" })
        .Build();

    private SneklDbContext CreateDb(ICurrentServer current)
    {
        var options = new DbContextOptionsBuilder<SneklDbContext>()
            .UseInMemoryDatabase(_dbName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new SneklDbContext(options, current);
    }

    private static string FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not find {relative} above {AppContext.BaseDirectory}.");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
