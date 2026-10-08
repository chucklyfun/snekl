using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Snekl.Account.Api.Controllers;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Data;
using Snekl.Account.Infrastructure.Security;
using Snekl.Domain.Identity.Did;
using Snekl.Domain.Security;
using DomainDtos = Snekl.Domain.Dtos;

namespace Snekl.Account.Api.Tests;

/// <summary>
/// Signed Content -> Auth business hosting channel: signature verification (valid, missing, invalid,
/// stale, replayed, untrusted signer, wrong audience) and the status / link business rules.
/// </summary>
[Collection(AccountApiCollection.Name)]
public class BusinessHostingTests
{
    private const string AdminSigningKey = "snekl-auth-admin-test-signing-key-min-32!!";

    // Trusted host pinned by key (did:web style identity).
    private const string TrustedDid = "did:web:content.test";
    private const string TrustedUrl = "http://content.test";
    private static readonly DidKey.KeyPair TrustedKeys = DidKey.Generate();

    // Trusted host identified by did:key, so its key comes from the DID itself.
    private const string KeyHostUrl = "http://content-key.test";
    private static readonly DidKey.KeyPair KeyHostKeys = DidKey.Generate();

    private static readonly Guid HostNodeId = Guid.NewGuid();

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly AccountApiFixture _fixture;

    public BusinessHostingTests(AccountApiFixture fixture) => _fixture = fixture;

    /// <summary>The shared test Auth plus a BusinessHosting trust list (same database, no reset).</summary>
    private WebApplicationFactory<AuthController> TrustingFactory() =>
        _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Snekl:ResetDb", "false");
            builder.UseSetting("BusinessHosting:TrustedContentServers:0:Name", "Test content");
            builder.UseSetting("BusinessHosting:TrustedContentServers:0:Did", TrustedDid);
            builder.UseSetting("BusinessHosting:TrustedContentServers:0:BaseUrl", TrustedUrl);
            builder.UseSetting(
                "BusinessHosting:TrustedContentServers:0:VerificationKeyMultibase",
                DidKey.PublicKeyMultibase(TrustedKeys.Did));
            builder.UseSetting("BusinessHosting:TrustedContentServers:1:Name", "Key content");
            builder.UseSetting("BusinessHosting:TrustedContentServers:1:Did", KeyHostKeys.Did);
            builder.UseSetting("BusinessHosting:TrustedContentServers:1:BaseUrl", KeyHostUrl);
            builder.UseSetting("BusinessHosting:TrustedContentServers:1:VerificationKeyMultibase", "");
        });

    private DomainDtos.BusinessHostingStatusQuery Query(
        string memberDid,
        Guid? businessId = null,
        string signerDid = TrustedDid,
        string? audience = null,
        string? nonce = null) =>
        new(signerDid, audience ?? _fixture.AuthBaseUrl, nonce ?? BusinessHostingEnvelope.NewNonce(), memberDid, businessId);

    private DomainDtos.BusinessHostingLinkCommand LinkCommand(
        string memberDid,
        Guid businessId,
        Guid serverId,
        string? serverDid = null) =>
        new(
            TrustedDid,
            _fixture.AuthBaseUrl,
            BusinessHostingEnvelope.NewNonce(),
            memberDid,
            businessId,
            serverId,
            serverDid ?? $"did:key:zServer{serverId:N}",
            TrustedUrl);

    private static DomainDtos.FederationEnvelope Seal(
        string type,
        DomainDtos.IBusinessHostingPayload payload,
        byte[]? privateKey = null,
        string sourceUrl = TrustedUrl,
        DateTime? timestamp = null) =>
        BusinessHostingEnvelope.Seal(privateKey ?? TrustedKeys.PrivateKey, type, HostNodeId, sourceUrl, payload, timestamp);

    private static Task<HttpResponseMessage> PostStatusAsync(HttpClient client, object? envelope) =>
        client.PostAsJsonAsync("/api/business/hosting/status", envelope, WebJson);

    private static Task<HttpResponseMessage> PostLinkAsync(HttpClient client, object? envelope) =>
        client.PostAsJsonAsync("/api/business/hosting/link", envelope, WebJson);

    private static async Task<string> CodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() ?? "" : "";
    }

    private HttpClient OperatorClient()
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AdminSigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _fixture.AuthBaseUrl,
            audience: AuthAdminAuth.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim("did", "did:key:zOperatorForTests"),
                new Claim("username", "op-hosting"),
                new Claim(AuthAdminAuth.RoleClaim, AuthAdminAuth.Role)
            ],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: creds);
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    private sealed record Member(Guid BusinessId, string Did, string Username, BusinessSignupRequest Request);

    /// <summary>Signs up a business; optionally approves or rejects it. Returns the owner's DID.</summary>
    private async Task<Member> BusinessAsync(string? review = null)
    {
        var username = $"bh_{Guid.NewGuid():N}"[..16];
        var request = new BusinessSignupRequest(
            BusinessName: "Hosting Co",
            ContactName: "Hana Host",
            Email: $"{username}@example.com",
            Username: username,
            Password: "Password1!",
            Services: [BusinessServiceKeys.ServerHosting]);
        var response = await _fixture.Client.PostAsJsonAsync("/api/business/signup", request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        var business = JsonSerializer.Deserialize<BusinessAccountDto>(body, WebJson)!;

        if (review is not null)
        {
            using var op = OperatorClient();
            var reviewed = await op.PostAsync($"/api/business/accounts/{business.Id}/{review}", null);
            Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
        }

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
        var did = await db.PersonClusters.Where(p => p.Id == business.PersonClusterId).Select(p => p.Did).SingleAsync();
        return new Member(business.Id, did, username, request);
    }

    private static async Task<DomainDtos.BusinessHostingStatus> ReadStatusAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<DomainDtos.BusinessHostingStatus>(body, WebJson)!;
    }

    // ---- Signature verification ----

    [Fact]
    public async Task Status_ValidSignature_ReturnsPendingBusiness()
    {
        var member = await BusinessAsync();
        await using var factory = TrustingFactory();
        using var client = factory.CreateClient();

        var status = await ReadStatusAsync(await PostStatusAsync(client,
            Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query(member.Did))));

        Assert.Equal(member.BusinessId, status.BusinessAccountId);
        Assert.Equal(DomainDtos.BusinessHostingStatusNames.Pending, status.Status);
        Assert.Equal(member.Username, status.Username);
        Assert.Equal(member.Did, status.MemberDid);
        Assert.Equal("Hosting Co", status.BusinessName);
        Assert.Null(status.ServerId);
    }

    [Fact]
    public async Task Status_DidKeySigner_KeyComesFromTheDid()
    {
        var member = await BusinessAsync("approve");
        await using var factory = TrustingFactory();
        using var client = factory.CreateClient();

        var envelope = Seal(
            DomainDtos.BusinessHostingEnvelopeTypes.Status,
            Query(member.Did, signerDid: KeyHostKeys.Did),
            KeyHostKeys.PrivateKey,
            KeyHostUrl);
        var status = await ReadStatusAsync(await PostStatusAsync(client, envelope));
        Assert.Equal(DomainDtos.BusinessHostingStatusNames.Approved, status.Status);
    }

    [Fact]
    public async Task Status_MissingSignature_Is401()
    {
        var member = await BusinessAsync();
        await using var factory = TrustingFactory();
        using var client = factory.CreateClient();

        var unsigned = Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query(member.Did)) with { Signature = null };
        var response = await PostStatusAsync(client, unsigned);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("signature_required", await CodeAsync(response));

        var empty = await client.PostAsync("/api/business/hosting/status",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, empty.StatusCode);
        Assert.Equal("signature_required", await CodeAsync(empty));

        var nothing = await client.PostAsync("/api/business/hosting/status",
            new StringContent("", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, nothing.StatusCode);
    }

    [Fact]
    public async Task Status_InvalidSignature_Is401()
    {
        var member = await BusinessAsync();
        var other = await BusinessAsync();
        await using var factory = TrustingFactory();
        using var client = factory.CreateClient();

        // Claims to be the trusted host but signs with some other key.
        var forged = Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query(member.Did), DidKey.Generate().PrivateKey);
        var response = await PostStatusAsync(client, forged);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("bad_signature", await CodeAsync(response));

        // Real signature, payload swapped afterwards (asks about someone else).
        var signed = Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query(member.Did));
        var tampered = signed with { Payload = Query(other.Did) };
        response = await PostStatusAsync(client, tampered);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("bad_signature", await CodeAsync(response));

        // Garbage signature string.
        response = await PostStatusAsync(client, signed with { Signature = "not-a-signature" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("bad_signature", await CodeAsync(response));
    }

    [Fact]
    public async Task Status_StaleOrFutureTimestamp_Is401()
    {
        var member = await BusinessAsync();
        await using var factory = TrustingFactory();
        using var client = factory.CreateClient();

        var stale = Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query(member.Did),
            timestamp: DateTime.UtcNow - FederationEnvelopeSigner.MaxEnvelopeAge - TimeSpan.FromMinutes(1));
        var response = await PostStatusAsync(client, stale);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("stale", await CodeAsync(response));

        var future = Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query(member.Did),
            timestamp: DateTime.UtcNow + FederationEnvelopeSigner.MaxClockSkew + TimeSpan.FromMinutes(1));
        response = await PostStatusAsync(client, future);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("stale", await CodeAsync(response));
    }

    [Fact]
    public async Task Status_ReplayedRequest_Is401()
    {
        var member = await BusinessAsync();
        await using var factory = TrustingFactory();
        using var client = factory.CreateClient();

        var envelope = Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query(member.Did));
        Assert.Equal(HttpStatusCode.OK, (await PostStatusAsync(client, envelope)).StatusCode);

        var replay = await PostStatusAsync(client, envelope);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal("replayed", await CodeAsync(replay));

        // Same nonce, freshly signed: still a replay.
        var nonce = BusinessHostingEnvelope.NewNonce();
        Assert.Equal(HttpStatusCode.OK, (await PostStatusAsync(client,
            Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query(member.Did, nonce: nonce)))).StatusCode);
        var resigned = await PostStatusAsync(client,
            Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query(member.Did, nonce: nonce)));
        Assert.Equal(HttpStatusCode.Unauthorized, resigned.StatusCode);
        Assert.Equal("replayed", await CodeAsync(resigned));
    }

    [Fact]
    public async Task Status_UntrustedSigner_Is403()
    {
        var member = await BusinessAsync();
        await using var factory = TrustingFactory();
        using var client = factory.CreateClient();

        // A well-formed, correctly signed request from a host Auth does not list.
        var stranger = DidKey.Generate();
        var response = await PostStatusAsync(client, Seal(
            DomainDtos.BusinessHostingEnvelopeTypes.Status,
            Query(member.Did, signerDid: stranger.Did),
            stranger.PrivateKey,
            "http://stranger.test"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("untrusted_signer", await CodeAsync(response));

        // Trusted DID but sent from a different base URL.
        response = await PostStatusAsync(client, Seal(
            DomainDtos.BusinessHostingEnvelopeTypes.Status,
            Query(member.Did),
            sourceUrl: "http://elsewhere.test"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("untrusted_signer", await CodeAsync(response));

        // Nothing is trusted on an Auth without the BusinessHosting list.
        using var plain = _fixture.Factory.CreateClient();
        response = await PostStatusAsync(plain, Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query(member.Did)));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Status_WrongAudienceOrType_IsRejected()
    {
        var member = await BusinessAsync();
        await using var factory = TrustingFactory();
        using var client = factory.CreateClient();

        var response = await PostStatusAsync(client, Seal(
            DomainDtos.BusinessHostingEnvelopeTypes.Status,
            Query(member.Did, audience: "http://another-auth.test")));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("wrong_audience", await CodeAsync(response));

        response = await PostStatusAsync(client, Seal(DomainDtos.BusinessHostingEnvelopeTypes.Link, Query(member.Did)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("wrong_type", await CodeAsync(response));
    }

    // ---- Status paths ----

    [Fact]
    public async Task Status_ReportsApprovedRejectedAndUnknown()
    {
        var approved = await BusinessAsync("approve");
        var rejected = await BusinessAsync("reject");
        await using var factory = TrustingFactory();
        using var client = factory.CreateClient();

        var a = await ReadStatusAsync(await PostStatusAsync(client,
            Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query(approved.Did, approved.BusinessId))));
        Assert.Equal(DomainDtos.BusinessHostingStatusNames.Approved, a.Status);

        var r = await ReadStatusAsync(await PostStatusAsync(client,
            Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query(rejected.Did))));
        Assert.Equal(DomainDtos.BusinessHostingStatusNames.Rejected, r.Status);

        var unknown = await PostStatusAsync(client,
            Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query("did:key:zNobodyHere")));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // A business id that belongs to someone else is not disclosed.
        var foreign = await PostStatusAsync(client,
            Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query(approved.Did, rejected.BusinessId)));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    // ---- Link paths ----

    [Fact]
    public async Task Link_IsIdempotent_AndShowsUpInStatusAndMe()
    {
        var member = await BusinessAsync("approve");
        await using var factory = TrustingFactory();
        using var client = factory.CreateClient();
        var serverId = Guid.NewGuid();

        var first = await PostLinkAsync(client, Seal(DomainDtos.BusinessHostingEnvelopeTypes.Link,
            LinkCommand(member.Did, member.BusinessId, serverId)));
        var firstBody = await first.Content.ReadAsStringAsync();
        Assert.True(first.StatusCode == HttpStatusCode.OK, firstBody);
        var linked = JsonSerializer.Deserialize<DomainDtos.BusinessHostingLinkResult>(firstBody, WebJson)!;
        Assert.True(linked.Changed);
        Assert.Equal(serverId, linked.Business.ServerId);

        // Retry (new nonce, same server): no change, still 200.
        var again = await PostLinkAsync(client, Seal(DomainDtos.BusinessHostingEnvelopeTypes.Link,
            LinkCommand(member.Did, member.BusinessId, serverId)));
        var againBody = await again.Content.ReadAsStringAsync();
        Assert.True(again.StatusCode == HttpStatusCode.OK, againBody);
        Assert.False(JsonSerializer.Deserialize<DomainDtos.BusinessHostingLinkResult>(againBody, WebJson)!.Changed);

        var status = await ReadStatusAsync(await PostStatusAsync(client,
            Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query(member.Did))));
        Assert.Equal(serverId, status.ServerId);
        Assert.Equal($"did:key:zServer{serverId:N}", status.ServerDid);
        Assert.Equal(TrustedUrl, status.ServerBaseUrl);

        var login = await _fixture.Client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(member.Request.Email, member.Request.Password));
        var session = await login.Content.ReadFromJsonAsync<RingSessionDto>(WebJson);
        using var owner = _fixture.CreateClient();
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);
        var mine = await owner.GetFromJsonAsync<List<BusinessAccountDto>>("/api/business/me", WebJson);
        var row = Assert.Single(mine!, b => b.Id == member.BusinessId);
        Assert.Equal(serverId, row.ServerId);
        Assert.Equal($"did:key:zServer{serverId:N}", row.ServerDid);
        Assert.NotNull(row.ServerLinkedAt);
    }

    [Fact]
    public async Task Link_SecondServerForSameBusiness_Is409()
    {
        var member = await BusinessAsync("approve");
        await using var factory = TrustingFactory();
        using var client = factory.CreateClient();
        var serverId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.OK, (await PostLinkAsync(client, Seal(DomainDtos.BusinessHostingEnvelopeTypes.Link,
            LinkCommand(member.Did, member.BusinessId, serverId)))).StatusCode);

        var second = await PostLinkAsync(client, Seal(DomainDtos.BusinessHostingEnvelopeTypes.Link,
            LinkCommand(member.Did, member.BusinessId, Guid.NewGuid())));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("already_has_server", await CodeAsync(second));

        var status = await ReadStatusAsync(await PostStatusAsync(client,
            Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query(member.Did))));
        Assert.Equal(serverId, status.ServerId);
    }

    [Fact]
    public async Task Link_PendingOrRejected_Is409()
    {
        var pending = await BusinessAsync();
        var rejected = await BusinessAsync("reject");
        await using var factory = TrustingFactory();
        using var client = factory.CreateClient();

        var response = await PostLinkAsync(client, Seal(DomainDtos.BusinessHostingEnvelopeTypes.Link,
            LinkCommand(pending.Did, pending.BusinessId, Guid.NewGuid())));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("not_approved", await CodeAsync(response));

        response = await PostLinkAsync(client, Seal(DomainDtos.BusinessHostingEnvelopeTypes.Link,
            LinkCommand(rejected.Did, rejected.BusinessId, Guid.NewGuid())));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("not_approved", await CodeAsync(response));
    }

    [Fact]
    public async Task Link_ServerAlreadyLinkedToAnotherBusiness_Is409_UniqueIndex()
    {
        var first = await BusinessAsync("approve");
        var second = await BusinessAsync("approve");
        await using var factory = TrustingFactory();
        using var client = factory.CreateClient();
        var serverId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.OK, (await PostLinkAsync(client, Seal(DomainDtos.BusinessHostingEnvelopeTypes.Link,
            LinkCommand(first.Did, first.BusinessId, serverId)))).StatusCode);

        var response = await PostLinkAsync(client, Seal(DomainDtos.BusinessHostingEnvelopeTypes.Link,
            LinkCommand(second.Did, second.BusinessId, serverId)));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("server_in_use", await CodeAsync(response));

        var status = await ReadStatusAsync(await PostStatusAsync(client,
            Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query(second.Did))));
        Assert.Null(status.ServerId);
    }

    [Fact]
    public async Task Link_OtherMembersBusiness_Is404_AndUnsignedIs401()
    {
        var owner = await BusinessAsync("approve");
        var intruder = await BusinessAsync("approve");
        await using var factory = TrustingFactory();
        using var client = factory.CreateClient();

        var response = await PostLinkAsync(client, Seal(DomainDtos.BusinessHostingEnvelopeTypes.Link,
            LinkCommand(intruder.Did, owner.BusinessId, Guid.NewGuid())));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var unsigned = Seal(DomainDtos.BusinessHostingEnvelopeTypes.Link,
            LinkCommand(owner.Did, owner.BusinessId, Guid.NewGuid())) with { Signature = null };
        response = await PostLinkAsync(client, unsigned);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var status = await ReadStatusAsync(await PostStatusAsync(client,
            Seal(DomainDtos.BusinessHostingEnvelopeTypes.Status, Query(owner.Did))));
        Assert.Null(status.ServerId);
    }
}
