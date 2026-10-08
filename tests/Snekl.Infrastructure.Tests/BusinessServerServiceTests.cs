using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Identity.Did;
using Snekl.Domain.Security;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

/// <summary>
/// Owner-created hosted server on the content side: approved / pending / rejected / already-has-server,
/// and recovery when the server was spawned but the link to Auth failed (no second server).
/// </summary>
public class BusinessServerServiceTests
{
    private const string Issuer = "http://auth.test";

    private sealed class FakeAuth : IBusinessHostingAuthClient
    {
        private readonly object _gate = new();
        public BusinessHostingStatus? Business { get; set; }
        public int StatusCode { get; set; } = 200;
        public int FailNextLinks { get; set; }
        public List<BusinessServerLink> Links { get; } = [];
        public int StatusCalls { get; private set; }

        public Task<BusinessHostingCallResult<BusinessHostingStatus>> GetStatusAsync(
            string issuerUrl, string memberDid, Guid? businessAccountId, CancellationToken ct = default)
        {
            lock (_gate)
            {
                StatusCalls++;
                Assert.Equal(Issuer, issuerUrl);
                if (StatusCode == 0)
                    return Task.FromResult(new BusinessHostingCallResult<BusinessHostingStatus>(0, null, "Could not reach the auth server.", "unreachable"));
                if (StatusCode != 200)
                    return Task.FromResult(new BusinessHostingCallResult<BusinessHostingStatus>(StatusCode, null, "rejected", "bad_signature"));
                if (Business is null || Business.MemberDid != memberDid)
                    return Task.FromResult(new BusinessHostingCallResult<BusinessHostingStatus>(404, null, "No business", "no_business"));
                return Task.FromResult(new BusinessHostingCallResult<BusinessHostingStatus>(200, Business));
            }
        }

        public Task<BusinessHostingCallResult<BusinessHostingLinkResult>> LinkAsync(
            string issuerUrl, BusinessServerLink link, CancellationToken ct = default)
        {
            lock (_gate)
            {
                Links.Add(link);
                if (FailNextLinks > 0)
                {
                    FailNextLinks--;
                    return Task.FromResult(new BusinessHostingCallResult<BusinessHostingLinkResult>(0, null, "Could not reach the auth server.", "unreachable"));
                }

                if (Business!.ServerId is Guid existing && existing != link.ServerId)
                    return Task.FromResult(new BusinessHostingCallResult<BusinessHostingLinkResult>(409, null, "already has a server", "already_has_server"));
                var changed = Business.ServerId is null;
                Business = Business with { ServerId = link.ServerId, ServerDid = link.ServerDid, ServerBaseUrl = link.ServerBaseUrl };
                return Task.FromResult(new BusinessHostingCallResult<BusinessHostingLinkResult>(200, new BusinessHostingLinkResult(Business, changed)));
            }
        }
    }

    private sealed class Harness : IDisposable
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public string TemplatePath { get; } = Path.Combine(Path.GetTempPath(), $"snekl-biz-template-{Guid.NewGuid():N}.json");
        public NodeConfig Host { get; } = new() { Name = "Host", BaseUrl = "http://content.test" };
        public Guid UserId { get; } = Guid.NewGuid();
        public string MemberDid { get; } = $"did:key:zMember{Guid.NewGuid():N}";
        public FakeAuth Auth { get; } = new();

        public Harness(string? issuer = Issuer, bool userWithoutServer = false)
        {
            Host.Did = "did:web:content.test";
            Host.CreatedAt = DateTime.UtcNow.AddDays(-1);
            using var db = CreateDb(out _);
            db.NodeConfig.Add(Host);
            db.Users.Add(new User
            {
                Id = UserId,
                ServerId = userWithoutServer ? Guid.Empty : Host.Id,
                Username = "owner",
                DisplayName = "Owner",
                Email = "owner@example.com",
                Did = MemberDid,
                PasswordHash = "x",
                Bio = "",
                AuthIssuerUrl = issuer
            });
            // The host trusts the auth server its members sign in through; a business server pins it.
            db.TrustedAuthIssuers.Add(new Snekl.Domain.Identity.TrustedAuthIssuer
            {
                ServerId = Host.Id,
                Name = "Auth",
                IssuerUrl = Issuer,
                JwksUrl = Issuer + "/.well-known/jwks.json"
            });
            db.SaveChanges();
        }

        public SneklDbContext CreateDb(out CurrentServer current)
        {
            current = new CurrentServer();
            var options = new DbContextOptionsBuilder<SneklDbContext>()
                .UseInMemoryDatabase(DbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var db = new SneklDbContext(options, current);
            return db;
        }

        /// <summary>A service as a request on the host's first server would see it.</summary>
        public BusinessServerService CreateService(out SneklDbContext db)
        {
            db = CreateDb(out var current);
            current.Use(Host.Id);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Node:BaseUrl"] = "http://content.test" })
                .Build();
            var servers = new VirtualServerService(db, new ServerTemplateStore(TemplatePath), config);
            return new BusinessServerService(db, Auth, servers);
        }

        public BusinessHostingStatus Business(string status, Guid? serverId = null) =>
            new(Guid.NewGuid(), "Acme Studio", status, MemberDid, "acme", serverId, serverId is null ? null : "did:key:zLinked", null);

        public int ServerCount()
        {
            using var db = CreateDb(out _);
            return db.NodeConfig.Count();
        }

        public void Dispose()
        {
            if (File.Exists(TemplatePath)) File.Delete(TemplatePath);
        }
    }

    [Fact]
    public async Task Approved_SpawnsOneServer_AndLinksIt()
    {
        using var h = new Harness();
        h.Auth.Business = h.Business(BusinessHostingStatusNames.Approved);
        var service = h.CreateService(out var db);

        var result = await service.CreateAsync(h.UserId, h.MemberDid, null);

        Assert.Equal(BusinessServerOutcome.Created, result.Outcome);
        Assert.NotNull(result.Server);
        Assert.Equal("Acme Studio", result.Server!.Name);
        Assert.Equal("acme", result.Server.OwnerUsername);
        Assert.Equal(2, h.ServerCount());
        var link = Assert.Single(h.Auth.Links);
        Assert.Equal(result.Server.Id, link.ServerId);
        Assert.Equal(result.Server.Did, link.ServerDid);
        Assert.Equal(h.Auth.Business!.BusinessAccountId, link.BusinessAccountId);
        Assert.Equal(h.MemberDid, link.MemberDid);

        var owner = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.ServerId == result.Server.Id);
        Assert.Equal(h.MemberDid, owner.Did);

        // The owner's auth server is pinned on the new server so it can't lose it.
        var pinned = await db.TrustedAuthIssuers.IgnoreQueryFilters().SingleAsync(i => i.ServerId == result.Server.Id);
        Assert.Equal(Issuer, pinned.IssuerUrl);
        Assert.True(pinned.IsRequired);
        Assert.True(pinned.IsActive);
        await db.DisposeAsync();
    }

    [Theory]
    [InlineData(BusinessHostingStatusNames.Pending)]
    [InlineData(BusinessHostingStatusNames.Rejected)]
    public async Task NotApproved_Refuses_WithoutSpawning(string status)
    {
        using var h = new Harness();
        h.Auth.Business = h.Business(status);
        var service = h.CreateService(out var db);

        var result = await service.CreateAsync(h.UserId, h.MemberDid, null);

        Assert.Equal(BusinessServerOutcome.NotApproved, result.Outcome);
        Assert.Equal(status, result.BusinessStatus);
        Assert.Equal(1, h.ServerCount());
        Assert.Empty(h.Auth.Links);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AlreadyHasServer_Refuses_WithoutSpawning()
    {
        using var h = new Harness();
        var linked = Guid.NewGuid();
        h.Auth.Business = h.Business(BusinessHostingStatusNames.Approved, linked);
        var service = h.CreateService(out var db);

        var result = await service.CreateAsync(h.UserId, h.MemberDid, null);

        Assert.Equal(BusinessServerOutcome.AlreadyHasServer, result.Outcome);
        Assert.Equal(linked, result.LinkedServerId);
        Assert.Equal("did:key:zLinked", result.LinkedServerDid);
        Assert.Equal(1, h.ServerCount());
        Assert.Empty(h.Auth.Links);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task NoBusiness_AuthDown_OrAuthRejects_DoNotSpawn()
    {
        using var h = new Harness();
        var service = h.CreateService(out var db);

        Assert.Equal(BusinessServerOutcome.NoBusiness, (await service.CreateAsync(h.UserId, h.MemberDid, null)).Outcome);

        h.Auth.Business = h.Business(BusinessHostingStatusNames.Approved);
        h.Auth.StatusCode = 0;
        Assert.Equal(BusinessServerOutcome.AuthUnavailable, (await service.CreateAsync(h.UserId, h.MemberDid, null)).Outcome);

        h.Auth.StatusCode = 401;
        Assert.Equal(BusinessServerOutcome.AuthRejected, (await service.CreateAsync(h.UserId, h.MemberDid, null)).Outcome);

        Assert.Equal(1, h.ServerCount());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task NoAuthIssuer_IsNotSignedIn()
    {
        using var h = new Harness(issuer: null);
        h.Auth.Business = h.Business(BusinessHostingStatusNames.Approved);
        var service = h.CreateService(out var db);

        var result = await service.CreateAsync(h.UserId, h.MemberDid, null);

        Assert.Equal(BusinessServerOutcome.NotSignedIn, result.Outcome);
        Assert.Equal(0, h.Auth.StatusCalls);
        Assert.Equal(1, h.ServerCount());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ShadowUserWithoutServerScope_StillFindsTheIssuer()
    {
        using var h = new Harness(userWithoutServer: true);
        h.Auth.Business = h.Business(BusinessHostingStatusNames.Approved);
        var service = h.CreateService(out var db);

        var result = await service.CreateAsync(h.UserId, h.MemberDid, null);

        Assert.Equal(BusinessServerOutcome.Created, result.Outcome);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task LinkFailure_RetryRelinksTheSameServer_NoDuplicate()
    {
        using var h = new Harness();
        h.Auth.Business = h.Business(BusinessHostingStatusNames.Approved);
        h.Auth.FailNextLinks = 1;

        var first = h.CreateService(out var db1);
        var failed = await first.CreateAsync(h.UserId, h.MemberDid, null);
        Assert.Equal(BusinessServerOutcome.LinkFailed, failed.Outcome);
        Assert.NotNull(failed.Server);
        Assert.Null(h.Auth.Business!.ServerId);
        Assert.Equal(2, h.ServerCount());
        await db1.DisposeAsync();

        // A later request (new scope) finishes the link instead of spawning again.
        var second = h.CreateService(out var db2);
        var retried = await second.CreateAsync(h.UserId, h.MemberDid, null);
        Assert.Equal(BusinessServerOutcome.Relinked, retried.Outcome);
        Assert.Equal(failed.Server!.Id, retried.Server!.Id);
        Assert.Equal(failed.Server.Id, h.Auth.Business!.ServerId);
        Assert.Equal(2, h.ServerCount());
        Assert.Equal(2, h.Auth.Links.Count);
        Assert.All(h.Auth.Links, l => Assert.Equal(failed.Server.Id, l.ServerId));

        // And once linked, further attempts are refused without touching servers.
        var third = await second.CreateAsync(h.UserId, h.MemberDid, null);
        Assert.Equal(BusinessServerOutcome.AlreadyHasServer, third.Outcome);
        Assert.Equal(2, h.ServerCount());
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task DisabledLeftoverServer_IsNotReused()
    {
        using var h = new Harness();
        h.Auth.Business = h.Business(BusinessHostingStatusNames.Approved);
        h.Auth.FailNextLinks = 1;
        var first = h.CreateService(out var db1);
        var failed = await first.CreateAsync(h.UserId, h.MemberDid, null);
        var leftover = await db1.NodeConfig.SingleAsync(n => n.Id == failed.Server!.Id);
        leftover.DisabledAt = DateTime.UtcNow;
        await db1.SaveChangesAsync();
        await db1.DisposeAsync();

        var second = h.CreateService(out var db2);
        var result = await second.CreateAsync(h.UserId, h.MemberDid, null);
        Assert.Equal(BusinessServerOutcome.Created, result.Outcome);
        Assert.NotEqual(failed.Server!.Id, result.Server!.Id);
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentRequests_SpawnOnlyOnce()
    {
        using var h = new Harness();
        h.Auth.Business = h.Business(BusinessHostingStatusNames.Approved);
        var a = h.CreateService(out var dbA);
        var b = h.CreateService(out var dbB);

        var results = await Task.WhenAll(
            Task.Run(() => a.CreateAsync(h.UserId, h.MemberDid, null)),
            Task.Run(() => b.CreateAsync(h.UserId, h.MemberDid, null)));

        Assert.Single(results, r => r.Outcome == BusinessServerOutcome.Created);
        Assert.Single(results, r => r.Outcome == BusinessServerOutcome.AlreadyHasServer);
        Assert.Equal(2, h.ServerCount());
        await dbA.DisposeAsync();
        await dbB.DisposeAsync();
    }
}

/// <summary>The real HTTP client signs as the host's first server and talks to the member's Auth.</summary>
public class BusinessHostingAuthClientTests
{
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, string, HttpResponseMessage> Respond { get; set; } =
            (_, _) => new HttpResponseMessage(HttpStatusCode.OK);
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request, body));
            return Respond(request, body);
        }
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static (SneklDbContext Db, NodeConfig Host, DidKey.KeyPair Keys) Setup()
    {
        var options = new DbContextOptionsBuilder<SneklDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new SneklDbContext(options, new CurrentServer());
        var keys = DidKey.Generate();
        var host = new NodeConfig
        {
            Name = "Host",
            BaseUrl = "http://content.test",
            Did = "did:web:content.test",
            VerificationKeyMultibase = DidKey.PublicKeyMultibase(keys.Did),
            VerificationPrivateKeyPkcs8 = Convert.ToBase64String(keys.PrivateKey),
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        // A virtual server created later must never be the signer.
        var later = DidKey.Generate();
        db.NodeConfig.Add(host);
        db.NodeConfig.Add(new NodeConfig
        {
            Name = "Virtual",
            BaseUrl = "http://content.test",
            Did = later.Did,
            VerificationKeyMultibase = DidKey.PublicKeyMultibase(later.Did),
            VerificationPrivateKeyPkcs8 = Convert.ToBase64String(later.PrivateKey),
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
        return (db, host, keys);
    }

    [Fact]
    public async Task GetStatus_SendsEnvelopeSignedByTheHost()
    {
        var (db, host, keys) = Setup();
        var handler = new CaptureHandler
        {
            Respond = (_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new BusinessHostingStatus(
                    Guid.NewGuid(), "Acme", "approved", "did:key:zMember", "acme", null, null, null), WebJson),
                    Encoding.UTF8, "application/json")
            }
        };
        var client = new BusinessHostingAuthClient(new HttpClient(handler), db);

        var result = await client.GetStatusAsync("http://auth.test/", "did:key:zMember", null);

        Assert.True(result.IsSuccess);
        Assert.Equal("approved", result.Value!.Status);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("http://auth.test/api/business/hosting/status", request.RequestUri!.ToString());

        var envelope = JsonSerializer.Deserialize<FederationEnvelope>(body, WebJson)!;
        Assert.Equal(BusinessHostingEnvelopeTypes.Status, envelope.Type);
        Assert.Equal(host.Id, envelope.SourceNodeId);
        Assert.Equal("http://content.test", envelope.SourceNodeUrl);
        var payload = (JsonElement)envelope.Payload;
        var query = payload.Deserialize<BusinessHostingStatusQuery>(WebJson)!;
        Assert.Equal(host.Did, query.SignerDid);
        Assert.Equal("http://auth.test", query.Audience);
        Assert.Equal("did:key:zMember", query.MemberDid);
        Assert.False(string.IsNullOrWhiteSpace(query.Nonce));
        Assert.True(FederationEnvelopeSigner.Verify(
            DidKey.PublicKeyMultibase(keys.Did), envelope.Type, envelope.SourceNodeId, envelope.SourceNodeUrl,
            envelope.Timestamp, payload, envelope.Signature!));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Link_ReportsErrorsAndUnreachableAuth()
    {
        var (db, _, _) = Setup();
        var handler = new CaptureHandler
        {
            Respond = (_, _) => new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("{\"error\":\"That server is already linked.\",\"code\":\"server_in_use\"}",
                    Encoding.UTF8, "application/json")
            }
        };
        var client = new BusinessHostingAuthClient(new HttpClient(handler), db);
        var link = new BusinessServerLink("did:key:zMember", Guid.NewGuid(), Guid.NewGuid(), "did:key:zServer", "http://content.test/");

        var conflict = await client.LinkAsync("http://auth.test", link);
        Assert.False(conflict.IsSuccess);
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal("server_in_use", conflict.Code);
        Assert.Equal("That server is already linked.", conflict.Error);

        var sent = JsonSerializer.Deserialize<FederationEnvelope>(Assert.Single(handler.Requests).Body, WebJson)!;
        var command = ((JsonElement)sent.Payload).Deserialize<BusinessHostingLinkCommand>(WebJson)!;
        Assert.Equal(BusinessHostingEnvelopeTypes.Link, sent.Type);
        Assert.Equal("http://content.test", command.ServerBaseUrl);

        handler.Respond = (_, _) => throw new HttpRequestException("connection refused");
        var down = await client.LinkAsync("http://auth.test", link);
        Assert.Equal(0, down.StatusCode);
        Assert.Equal("unreachable", down.Code);
        await db.DisposeAsync();
    }
}
