using Microsoft.EntityFrameworkCore;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;
using Snekl.Account.Infrastructure.Security;

namespace Snekl.Account.Infrastructure.Services;

public interface IPartnerService
{
    Task<PartnerTrustDto> EstablishTrustAsync(string partnerBaseUrl, CancellationToken ct = default);
    Task AcknowledgeIncomingTrustAsync(IncomingPartnerTrustRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PartnerTrustDto>> ListTrustsAsync(CancellationToken ct = default);
    Task<PartnerSessionDto> ProvisionOnPartnerAsync(Guid identityId, ProvisionPartnerRequest request, CancellationToken ct = default);
    Task<PartnerSessionDto> AcceptVouchAsync(VouchPayload vouch, string? password, CancellationToken ct = default);
    Task<PartnerSessionDto?> AcceptRingSessionAsync(VouchPayload vouch, CancellationToken ct = default);
}

public class PartnerService : IPartnerService
{
    private readonly AccountDbContext _db;
    private readonly IAuthServerService _server;
    private readonly ITokenService _tokens;
    private readonly IPartnerClient _client;

    public PartnerService(
        AccountDbContext db,
        IAuthServerService server,
        ITokenService tokens,
        IPartnerClient client)
    {
        _db = db;
        _server = server;
        _tokens = tokens;
        _client = client;
    }

    public async Task<PartnerTrustDto> EstablishTrustAsync(string partnerBaseUrl, CancellationToken ct = default)
    {
        partnerBaseUrl = partnerBaseUrl.Trim().TrimEnd('/');
        var config = await _server.GetOrCreateAsync(ct);

        var trust = await _db.PartnerTrusts.FirstOrDefaultAsync(t => t.PartnerBaseUrl == partnerBaseUrl, ct);
        if (trust is null)
        {
            trust = new AuthPartnerTrust { PartnerBaseUrl = partnerBaseUrl, LocalAcknowledged = true };
            _db.PartnerTrusts.Add(trust);
        }
        else
        {
            trust.LocalAcknowledged = true;
        }

        await _db.SaveChangesAsync(ct);

        await _client.NotifyIncomingTrustAsync(partnerBaseUrl, new IncomingPartnerTrustRequest(config.BaseUrl, config.Name, config.SigningKey), ct);

        return ToDto(trust);
    }

    public async Task AcknowledgeIncomingTrustAsync(IncomingPartnerTrustRequest request, CancellationToken ct = default)
    {
        var partnerBaseUrl = request.PartnerBaseUrl.Trim().TrimEnd('/');
        var trust = await _db.PartnerTrusts.FirstOrDefaultAsync(t => t.PartnerBaseUrl == partnerBaseUrl, ct);
        if (trust is null)
        {
            trust = new AuthPartnerTrust
            {
                PartnerBaseUrl = partnerBaseUrl,
                PartnerSigningKey = request.PartnerSigningKey,
                RemoteAcknowledged = true
            };
            _db.PartnerTrusts.Add(trust);
        }
        else
        {
            trust.RemoteAcknowledged = true;
            trust.PartnerSigningKey = request.PartnerSigningKey;
        }

        await _db.SaveChangesAsync(ct);

        var config = await _server.GetOrCreateAsync(ct);
        if (!trust.LocalAcknowledged)
        {
            trust.LocalAcknowledged = true;
            await _db.SaveChangesAsync(ct);
            await _client.NotifyIncomingTrustAsync(partnerBaseUrl, new IncomingPartnerTrustRequest(config.BaseUrl, config.Name, config.SigningKey), ct);
        }
    }

    public async Task<IReadOnlyList<PartnerTrustDto>> ListTrustsAsync(CancellationToken ct = default)
    {
        var trusts = await _db.PartnerTrusts.OrderBy(t => t.PartnerBaseUrl).ToListAsync(ct);
        return trusts.Select(ToDto).ToList();
    }

    public async Task<PartnerSessionDto> ProvisionOnPartnerAsync(Guid identityId, ProvisionPartnerRequest request, CancellationToken ct = default)
    {
        var config = await _server.GetOrCreateAsync(ct);
        var identity = await _db.AuthIdentities.Include(i => i.PersonCluster).FirstOrDefaultAsync(i => i.Id == identityId, ct)
            ?? throw new InvalidOperationException("Identity not found.");

        var cluster = identity.PersonCluster!;
        var partnerBaseUrl = request.PartnerBaseUrl.Trim().TrimEnd('/');

        var trust = await _db.PartnerTrusts.FirstOrDefaultAsync(t =>
            t.PartnerBaseUrl == partnerBaseUrl && t.LocalAcknowledged && t.RemoteAcknowledged, ct)
            ?? throw new InvalidOperationException("Mutual partner trust required.");

        var expiry = DateTime.UtcNow.AddMinutes(15);
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

        var session = await _client.ProvisionAndLoginAsync(partnerBaseUrl, vouch, request.Password, ct)
            ?? throw new InvalidOperationException("Partner provisioning failed.");

        return session;
    }

    public async Task<PartnerSessionDto> AcceptVouchAsync(VouchPayload vouch, string? password, CancellationToken ct = default)
    {
        var config = await _server.GetOrCreateAsync(ct);

        if (!await ValidatePartnerVouchAsync(vouch, ct))
            throw new InvalidOperationException("Invalid or untrusted vouch.");

        var cluster = await _db.PersonClusters
            .Include(c => c.Identities)
            .FirstOrDefaultAsync(c => c.Did == vouch.PersonDid || c.Id == vouch.PersonClusterId, ct);

        if (cluster is null)
        {
            cluster = new PersonCluster
            {
                Id = vouch.PersonClusterId,
                Did = vouch.PersonDid,
                PublicKeyMultibase = Snekl.Domain.Identity.Did.DidKey.PublicKeyMultibase(vouch.PersonDid),
                CanonicalUsername = vouch.CanonicalUsername,
                DisplayName = vouch.DisplayName,
                Email = vouch.Email,
                Bio = vouch.Bio,
                DisambiguationTag = vouch.DisambiguationTag,
                PrimaryAuthServerUrl = vouch.SourceAuthServerUrl
            };
            _db.PersonClusters.Add(cluster);
            _db.SettingsReplicas.Add(new UserSettingsReplica { PersonClusterId = cluster.Id });
        }

        var existingIdentity = cluster.Identities.FirstOrDefault(i => i.AuthServerUrl == config.BaseUrl);
        AuthIdentity identity;
        if (existingIdentity is null)
        {
            identity = new AuthIdentity
            {
                PersonClusterId = cluster.Id,
                AuthServerUrl = config.BaseUrl,
                PasswordHash = string.IsNullOrWhiteSpace(password) ? null : BCrypt.Net.BCrypt.HashPassword(password),
                IsPrimary = cluster.Identities.Count == 0
            };
            _db.AuthIdentities.Add(identity);
        }
        else
        {
            identity = existingIdentity;
            if (!string.IsNullOrWhiteSpace(password))
                identity.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        }

        await _db.SaveChangesAsync(ct);

        var token = _tokens.IssueToken(identity, cluster, config.BaseUrl);
        return new PartnerSessionDto(config.BaseUrl, token, vouch.ExpiresAt);
    }

    public async Task<PartnerSessionDto?> AcceptRingSessionAsync(VouchPayload vouch, CancellationToken ct = default)
    {
        try
        {
            return await AcceptVouchAsync(vouch, null, ct);
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> ValidatePartnerVouchAsync(VouchPayload vouch, CancellationToken ct)
    {
        if (vouch.ExpiresAt <= DateTime.UtcNow) return false;

        var trust = await _db.PartnerTrusts.FirstOrDefaultAsync(t =>
            t.PartnerBaseUrl == vouch.SourceAuthServerUrl.TrimEnd('/') &&
            t.LocalAcknowledged && t.RemoteAcknowledged, ct);

        if (trust is null) return false;

        if (string.IsNullOrWhiteSpace(trust.PartnerSigningKey)) return false;

        return VouchBuilder.TryValidate(vouch, trust.PartnerSigningKey);
    }

    private static PartnerTrustDto ToDto(AuthPartnerTrust trust) => new(
        trust.PartnerBaseUrl,
        trust.IsMutual,
        trust.CreatedAt);
}
