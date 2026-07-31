using System.Net.Http.Json;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;
using Snekl.Account.Infrastructure.Security;

namespace Snekl.Account.Infrastructure.Services;

public interface IAccountAuthService
{
    Task<RingSessionDto> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<RingSessionDto?> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthIdentity?> GetIdentityAsync(Guid identityId, CancellationToken ct = default);
}

public class AccountAuthService : IAccountAuthService
{
    private readonly AccountDbContext _db;
    private readonly IAuthServerService _server;
    private readonly ITokenService _tokens;
    private readonly IRingLoginService _ring;

    public AccountAuthService(
        AccountDbContext db,
        IAuthServerService server,
        ITokenService tokens,
        IRingLoginService ring)
    {
        _db = db;
        _server = server;
        _tokens = tokens;
        _ring = ring;
    }

    public async Task<RingSessionDto> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var config = await _server.GetOrCreateAsync(ct);
        var username = request.Username.Trim().ToLowerInvariant();

        if (await _db.PersonClusters.AnyAsync(c => c.CanonicalUsername == username, ct))
            throw new InvalidOperationException("Username already exists on this auth server.");

        var cluster = new PersonCluster
        {
            CanonicalUsername = username,
            DisplayName = request.DisplayName.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            PrimaryAuthServerUrl = config.BaseUrl
        };

        var identity = new AuthIdentity
        {
            PersonCluster = cluster,
            AuthServerUrl = config.BaseUrl,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            IsPrimary = true
        };

        _db.PersonClusters.Add(cluster);
        _db.AuthIdentities.Add(identity);
        _db.SettingsReplicas.Add(new UserSettingsReplica { PersonCluster = cluster });
        await _db.SaveChangesAsync(ct);

        return await _ring.CreateRingSessionAsync(identity, cluster, config, ct);
    }

    public async Task<RingSessionDto?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var config = await _server.GetOrCreateAsync(ct);
        var username = request.Username.Trim().ToLowerInvariant();

        var identity = await _db.AuthIdentities
            .Include(i => i.PersonCluster)
            .ThenInclude(c => c!.Identities)
            .FirstOrDefaultAsync(i =>
                i.AuthServerUrl == config.BaseUrl &&
                i.PersonCluster!.CanonicalUsername == username, ct);

        if (identity?.PasswordHash is null || !BCrypt.Net.BCrypt.Verify(request.Password, identity.PasswordHash))
            return null;

        return await _ring.CreateRingSessionAsync(identity, identity.PersonCluster!, config, ct);
    }

    public Task<AuthIdentity?> GetIdentityAsync(Guid identityId, CancellationToken ct = default) =>
        _db.AuthIdentities.Include(i => i.PersonCluster).FirstOrDefaultAsync(i => i.Id == identityId, ct);
}

public interface IRingLoginService
{
    Task<RingSessionDto> CreateRingSessionAsync(AuthIdentity identity, PersonCluster cluster, AuthServerConfig config, CancellationToken ct = default);
}

public class RingLoginService : IRingLoginService
{
    private readonly AccountDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IPartnerClient _partners;

    public RingLoginService(AccountDbContext db, ITokenService tokens, IPartnerClient partners)
    {
        _db = db;
        _tokens = tokens;
        _partners = partners;
    }

    public async Task<RingSessionDto> CreateRingSessionAsync(
        AuthIdentity identity,
        PersonCluster cluster,
        AuthServerConfig config,
        CancellationToken ct = default)
    {
        var expiry = DateTime.UtcNow.AddDays(7);
        var localToken = _tokens.IssueToken(identity, cluster, config.BaseUrl);

        var ringSession = new RingSession
        {
            PersonClusterId = cluster.Id,
            PrimaryIdentityId = identity.Id,
            ExpiresAt = expiry
        };
        _db.RingSessions.Add(ringSession);

        var partnerSessions = new List<PartnerSessionDto>();
        var mutualPartners = await _db.PartnerTrusts
            .Where(t => t.LocalAcknowledged && t.RemoteAcknowledged)
            .ToListAsync(ct);

        var vouch = VouchBuilder.Create(new UnsignedVouch(
            cluster.Id,
            identity.Id,
            config.BaseUrl,
            cluster.CanonicalUsername,
            cluster.DisplayName,
            cluster.Email,
            cluster.Bio,
            cluster.DisambiguationTag,
            expiry), config.SigningKey);

        foreach (var partner in mutualPartners)
        {
            var session = await _partners.RequestRingSessionAsync(partner.PartnerBaseUrl, vouch, ct);
            if (session is not null)
                partnerSessions.Add(session);
        }

        await _db.SaveChangesAsync(ct);

        var identities = await _db.AuthIdentities.Where(i => i.PersonClusterId == cluster.Id).ToListAsync(ct);
        return new RingSessionDto(
            ringSession.Id,
            localToken,
            expiry,
            PersonClusterMapper.ToDto(cluster, identities),
            partnerSessions);
    }
}

public interface IPartnerClient
{
    Task<PartnerSessionDto?> RequestRingSessionAsync(string partnerBaseUrl, VouchPayload vouch, CancellationToken ct = default);
    Task<bool> NotifyIncomingTrustAsync(string partnerBaseUrl, IncomingPartnerTrustRequest request, CancellationToken ct = default);
    Task<PartnerSessionDto?> ProvisionAndLoginAsync(string partnerBaseUrl, VouchPayload vouch, string? password, CancellationToken ct = default);
}

public class PartnerClient : IPartnerClient
{
    private readonly HttpClient _http;

    public PartnerClient(HttpClient http) => _http = http;

    public async Task<PartnerSessionDto?> RequestRingSessionAsync(string partnerBaseUrl, VouchPayload vouch, CancellationToken ct = default)
    {
        try
        {
            var url = $"{partnerBaseUrl.TrimEnd('/')}/api/ring/session";
            var response = await _http.PostAsJsonAsync(url, vouch, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<PartnerSessionDto>(cancellationToken: ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> NotifyIncomingTrustAsync(string partnerBaseUrl, IncomingPartnerTrustRequest request, CancellationToken ct = default)
    {
        try
        {
            var url = $"{partnerBaseUrl.TrimEnd('/')}/api/partners/trust/incoming";
            var response = await _http.PostAsJsonAsync(url, request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<PartnerSessionDto?> ProvisionAndLoginAsync(string partnerBaseUrl, VouchPayload vouch, string? password, CancellationToken ct = default)
    {
        try
        {
            var url = $"{partnerBaseUrl.TrimEnd('/')}/api/partners/provision";
            var response = await _http.PostAsJsonAsync(url, new { vouch, password }, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<PartnerSessionDto>(cancellationToken: ct);
        }
        catch
        {
            return null;
        }
    }
}
