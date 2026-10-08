using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;
using Snekl.Account.Infrastructure.Security;
using Snekl.Domain.Identity.Did;

namespace Snekl.Account.Infrastructure.Services;

public interface IAccountAuthService
{
    Task<RingSessionDto> RegisterAsync(RegisterRequest request, string deviceKeyThumbprint, CancellationToken ct = default);
    Task<RingSessionDto> ImportAsync(ImportIdentityRequest request, string deviceKeyThumbprint, CancellationToken ct = default);
    Task<AccountLoginResult?> LoginAsync(LoginRequest request, string deviceKeyThumbprint, CancellationToken ct = default);
    Task<AccountReauthResult> ReauthenticateAsync(Guid identityId, string password, string? twoFactorCode, string? deviceKeyThumbprint, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task DevResetPasswordAsync(DevResetPasswordRequest request, CancellationToken ct = default);
    Task<AuthIdentity?> GetIdentityAsync(Guid identityId, CancellationToken ct = default);
}

public class AccountAuthService : IAccountAuthService
{
    private readonly AccountDbContext _db;
    private readonly IAuthServerService _server;
    private readonly ITokenService _tokens;
    private readonly IRingLoginService _ring;
    private readonly IIdentityCredentialService _credentials;
    private readonly IEmailVerificationService _verification;
    private readonly ITwoFactorService _twoFactor;

    public AccountAuthService(
        AccountDbContext db,
        IAuthServerService server,
        ITokenService tokens,
        IRingLoginService ring,
        IIdentityCredentialService credentials,
        IEmailVerificationService verification,
        ITwoFactorService twoFactor)
    {
        _db = db;
        _server = server;
        _tokens = tokens;
        _ring = ring;
        _credentials = credentials;
        _verification = verification;
        _twoFactor = twoFactor;
    }

    public async Task<RingSessionDto> RegisterAsync(RegisterRequest request, string deviceKeyThumbprint, CancellationToken ct = default)
    {
        var config = await _server.GetOrCreateAsync(ct);
        var username = request.Username.Trim().ToLowerInvariant();

        if (await _db.PersonClusters.AnyAsync(c => c.CanonicalUsername == username, ct))
            throw new InvalidOperationException("Username already exists on this auth server.");

        var email = request.Email.Trim().ToLowerInvariant();

        string? generatedPrivateKey = null;
        string did;
        string publicKeyMultibase;

        if (!string.IsNullOrWhiteSpace(request.Did))
        {
            did = request.Did.Trim();
            if (!DidKey.TryParse(did, out _))
                throw new InvalidOperationException("Invalid did:key.");

            if (await _db.PersonClusters.AnyAsync(c => c.Did == did, ct))
                throw new InvalidOperationException("DID already registered on this auth server.");

            publicKeyMultibase = DidKey.PublicKeyMultibase(did);
            var challenge = $"snekl-register:{username}:{did}";
            if (string.IsNullOrWhiteSpace(request.KeyOwnershipProof) ||
                !DidKey.VerifyMultibaseProof(did, challenge, request.KeyOwnershipProof))
                throw new InvalidOperationException("Invalid DID ownership proof.");
        }
        else
        {
            var keyPair = DidKey.Generate();
            did = keyPair.Did;
            publicKeyMultibase = DidKey.PublicKeyMultibase(did);
            generatedPrivateKey = Convert.ToBase64String(keyPair.PrivateKey);
        }

        var cluster = new PersonCluster
        {
            Did = did,
            PublicKeyMultibase = publicKeyMultibase,
            CanonicalUsername = username,
            DisplayName = request.DisplayName.Trim(),
            Email = email,
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

        await _verification.EnsureVerifiedOrSendAsync(cluster, ct);

        var session = await _ring.CreateRingSessionAsync(identity, cluster, config, ct, DateTimeOffset.UtcNow, deviceKeyThumbprint);
        return session with { GeneratedKeyPrivatePkcs8Base64 = generatedPrivateKey };
    }

    public async Task<RingSessionDto> ImportAsync(ImportIdentityRequest request, string deviceKeyThumbprint, CancellationToken ct = default)
    {
        var config = await _server.GetOrCreateAsync(ct);
        var username = request.Username.Trim().ToLowerInvariant();
        var credential = _credentials.Parse(request.IdentityCredentialJson)
            ?? throw new InvalidOperationException("Invalid identity credential.");

        if (!await _credentials.ValidateAsync(credential, ct))
            throw new InvalidOperationException("Identity credential validation failed.");

        var subject = credential.CredentialSubject;
        if (await _db.PersonClusters.AnyAsync(c => c.Did == subject.Id, ct))
            throw new InvalidOperationException("DID already registered on this auth server.");

        if (await _db.PersonClusters.AnyAsync(c => c.CanonicalUsername == username, ct))
            throw new InvalidOperationException("Username already exists on this auth server.");

        var email = request.Email.Trim().ToLowerInvariant();

        var cluster = new PersonCluster
        {
            Did = subject.Id,
            PublicKeyMultibase = DidKey.PublicKeyMultibase(subject.Id),
            CanonicalUsername = username,
            DisplayName = request.DisplayName.Trim(),
            Email = email,
            Bio = subject.Bio,
            DisambiguationTag = subject.DisambiguationTag,
            PrimaryAuthServerUrl = subject.PrimaryAuthServerUrl
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

        await _verification.EnsureVerifiedOrSendAsync(cluster, ct);

        return await _ring.CreateRingSessionAsync(identity, cluster, config, ct, DateTimeOffset.UtcNow, deviceKeyThumbprint);
    }

    public async Task<AccountLoginResult?> LoginAsync(LoginRequest request, string deviceKeyThumbprint, CancellationToken ct = default)
    {
        var config = await _server.GetOrCreateAsync(ct);
        var email = request.Email.Trim().ToLowerInvariant();
        var serverUrl = config.BaseUrl.Trim().TrimEnd('/');

        var candidates = await _db.AuthIdentities
            .Include(i => i.PersonCluster)
            .Where(i => i.PersonCluster!.Email == email && i.PasswordHash != null)
            .ToListAsync(ct);

        var identity = candidates
            .Where(i => BCrypt.Net.BCrypt.Verify(request.Password, i.PasswordHash!))
            .OrderByDescending(i =>
                string.Equals(i.AuthServerUrl?.Trim().TrimEnd('/'), serverUrl, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(i => i.PersonCluster!.EmailVerifiedAt != null)
            .ThenByDescending(i => i.PersonCluster!.CreatedAt)
            .FirstOrDefault();

        if (identity is null)
            return null;

        if (!string.Equals(identity.AuthServerUrl?.Trim().TrimEnd('/'), serverUrl, StringComparison.OrdinalIgnoreCase))
        {
            identity.AuthServerUrl = serverUrl;
            await _db.SaveChangesAsync(ct);
        }

        if (identity.TotpEnabledAt is not null)
        {
            if (string.IsNullOrEmpty(identity.TotpSecretProtected))
                throw new InvalidOperationException("Two-factor authentication is misconfigured for this account.");
            return await _twoFactor.BeginLoginChallengeAsync(identity, deviceKeyThumbprint, ct);
        }

        var session = await _ring.CreateRingSessionAsync(identity, identity.PersonCluster!, config, ct, DateTimeOffset.UtcNow, deviceKeyThumbprint);
        return AccountLoginResult.SignedIn(session);
    }

    public async Task<AccountReauthResult> ReauthenticateAsync(
        Guid identityId,
        string password,
        string? twoFactorCode,
        string? deviceKeyThumbprint,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(password))
            return AccountReauthResult.Failed(AccountReauthStatus.InvalidPassword);

        var identity = await GetIdentityAsync(identityId, ct);
        if (identity?.PasswordHash is null || identity.PersonCluster is null)
            return AccountReauthResult.Failed(AccountReauthStatus.InvalidPassword);

        if (!BCrypt.Net.BCrypt.Verify(password, identity.PasswordHash))
            return AccountReauthResult.Failed(AccountReauthStatus.InvalidPassword);

        if (identity.TotpEnabledAt is not null)
        {
            var proof = await _twoFactor.CheckCodeAsync(identity, twoFactorCode, allowRecovery: true, ct);
            switch (proof)
            {
                case TwoFactorProof.Missing:
                    return AccountReauthResult.Failed(AccountReauthStatus.TwoFactorRequired);
                case TwoFactorProof.Invalid:
                    return AccountReauthResult.Failed(AccountReauthStatus.TwoFactorInvalid);
                case TwoFactorProof.Accepted:
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected two-factor result {proof}.");
            }
        }

        var config = await _server.GetOrCreateAsync(ct);
        var session = await _ring.CreateRingSessionAsync(identity, identity.PersonCluster, config, ct, DateTimeOffset.UtcNow, deviceKeyThumbprint);
        return AccountReauthResult.Succeeded(session);
    }

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
    {
        email = email.Trim().ToLowerInvariant();
        return _db.PersonClusters.AnyAsync(c => c.Email == email, ct);
    }

    public async Task DevResetPasswordAsync(DevResetPasswordRequest request, CancellationToken ct = default)
    {
        var config = await _server.GetOrCreateAsync(ct);
        var email = request.Email.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Email is required.");

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            throw new InvalidOperationException("New password must be at least 6 characters.");

        var candidates = await _db.AuthIdentities
            .Include(i => i.PersonCluster)
            .Where(i => i.PersonCluster!.Email == email)
            .ToListAsync(ct);

        var identity = candidates
            .OrderByDescending(i => i.AuthServerUrl == config.BaseUrl)
            .ThenByDescending(i => i.PersonCluster!.CreatedAt)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No account with that email. In dev the auth database is wiped when the auth server restarts — create a new account at /register, or use the exact email from signup.");

        identity.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _twoFactor.InvalidateLoginChallengesAsync(identity.Id, ct);
        await _db.SaveChangesAsync(ct);
    }

    public Task<AuthIdentity?> GetIdentityAsync(Guid identityId, CancellationToken ct = default) =>
        _db.AuthIdentities.Include(i => i.PersonCluster).FirstOrDefaultAsync(i => i.Id == identityId, ct);
}

public interface IRingLoginService
{
    Task<RingSessionDto> CreateRingSessionAsync(
        AuthIdentity identity,
        PersonCluster cluster,
        AuthServerConfig config,
        CancellationToken ct = default,
        DateTimeOffset? authTime = null,
        string? deviceKeyThumbprint = null);
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
        CancellationToken ct = default,
        DateTimeOffset? authTime = null,
        string? deviceKeyThumbprint = null)
    {
        var expiry = DateTime.UtcNow.AddDays(7);
        var localToken = _tokens.IssueToken(
            identity,
            cluster,
            config.BaseUrl,
            new TokenIssueOptions(AuthTime: authTime, DeviceKeyThumbprint: deviceKeyThumbprint));

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
            cluster.Did,
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
