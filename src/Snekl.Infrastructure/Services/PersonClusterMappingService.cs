using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Identity;

namespace Snekl.Infrastructure.Services;

public interface IPersonClusterMappingService
{
    Task<PersonClusterRefDto?> GetClusterAsync(Guid clusterId, CancellationToken ct = default);
    Task<PersonClusterRefDto?> GetClusterByDidAsync(string did, CancellationToken ct = default);
    Task<PersonClusterRefDto> MapIdentityAsync(MapIdentityRequest request, CancellationToken ct = default);
    Task<Guid?> ResolveClusterIdAsync(string issuerUrl, Guid subjectId, CancellationToken ct = default);
    Task<string?> ResolveDidAsync(string issuerUrl, Guid subjectId, CancellationToken ct = default);
    Task<IReadOnlyList<TrustedAuthIssuer>> ListTrustedIssuersAsync(CancellationToken ct = default);
    Task<TrustedAuthIssuer> RegisterTrustedIssuerAsync(RegisterTrustedIssuerRequest request, CancellationToken ct = default);
}

public class PersonClusterMappingService : IPersonClusterMappingService
{
    private readonly Data.SneklDbContext _db;

    public PersonClusterMappingService(Data.SneklDbContext db) => _db = db;

    public async Task<PersonClusterRefDto?> GetClusterAsync(Guid clusterId, CancellationToken ct = default)
    {
        var record = await _db.PersonClusterRecords.FindAsync([clusterId], ct);
        return record is null ? null : ToDto(record);
    }

    public async Task<PersonClusterRefDto?> GetClusterByDidAsync(string did, CancellationToken ct = default)
    {
        var record = await _db.PersonClusterRecords.FirstOrDefaultAsync(r => r.Did == did, ct);
        return record is null ? null : ToDto(record);
    }

    public async Task<PersonClusterRefDto> MapIdentityAsync(MapIdentityRequest request, CancellationToken ct = default)
    {
        var issuerUrl = request.IssuerUrl.Trim().TrimEnd('/');
        var cluster = await _db.PersonClusterRecords.FindAsync([request.PersonClusterId], ct);
        if (cluster is null)
        {
            cluster = new PersonClusterRecord
            {
                Id = request.PersonClusterId,
                Did = request.AuthorDid,
                CanonicalUsername = request.CanonicalUsername,
                DisplayName = request.DisplayName,
                PrimaryAuthServerUrl = request.PrimaryAuthServerUrl,
                DisambiguationTag = request.DisambiguationTag
            };
            _db.PersonClusterRecords.Add(cluster);
        }
        else
        {
            cluster.Did = request.AuthorDid;
            cluster.CanonicalUsername = request.CanonicalUsername;
            cluster.DisplayName = request.DisplayName;
            cluster.PrimaryAuthServerUrl = request.PrimaryAuthServerUrl;
            cluster.DisambiguationTag = request.DisambiguationTag;
            cluster.UpdatedAt = DateTime.UtcNow;
        }

        var existing = await _db.AuthorIdentityMaps.FirstOrDefaultAsync(m =>
            m.IssuerUrl == issuerUrl && m.SubjectId == request.SubjectId, ct);

        if (existing is null)
        {
            _db.AuthorIdentityMaps.Add(new AuthorIdentityMap
            {
                PersonClusterId = request.PersonClusterId,
                AuthorDid = request.AuthorDid,
                IssuerUrl = issuerUrl,
                SubjectId = request.SubjectId
            });
        }
        else
        {
            existing.PersonClusterId = request.PersonClusterId;
            existing.AuthorDid = request.AuthorDid;
            existing.MappedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return ToDto(cluster);
    }

    public async Task<Guid?> ResolveClusterIdAsync(string issuerUrl, Guid subjectId, CancellationToken ct = default)
    {
        var map = await _db.AuthorIdentityMaps.FirstOrDefaultAsync(m =>
            m.IssuerUrl == issuerUrl.Trim().TrimEnd('/') && m.SubjectId == subjectId, ct);
        return map?.PersonClusterId;
    }

    public async Task<string?> ResolveDidAsync(string issuerUrl, Guid subjectId, CancellationToken ct = default)
    {
        var map = await _db.AuthorIdentityMaps.FirstOrDefaultAsync(m =>
            m.IssuerUrl == issuerUrl.Trim().TrimEnd('/') && m.SubjectId == subjectId, ct);
        return map?.AuthorDid;
    }

    public async Task<IReadOnlyList<TrustedAuthIssuer>> ListTrustedIssuersAsync(CancellationToken ct = default) =>
        (await _db.EffectiveTrustedIssuersAsync(ct)).OrderBy(i => i.Name, StringComparer.Ordinal).ToList();

    public async Task<TrustedAuthIssuer> RegisterTrustedIssuerAsync(RegisterTrustedIssuerRequest request, CancellationToken ct = default)
    {
        var issuerUrl = request.IssuerUrl.Trim().TrimEnd('/');
        var existing = await _db.TrustedAuthIssuers.FirstOrDefaultAsync(i => i.IssuerUrl == issuerUrl, ct);
        if (existing is not null)
        {
            existing.JwksUrl = request.JwksUrl.Trim();
            existing.Name = request.Name.Trim();
            existing.IsActive = true;
            await _db.SaveChangesAsync(ct);
            return existing;
        }

        var issuer = new TrustedAuthIssuer
        {
            IssuerUrl = issuerUrl,
            JwksUrl = request.JwksUrl.Trim(),
            Name = request.Name.Trim()
        };
        _db.TrustedAuthIssuers.Add(issuer);
        await _db.SaveChangesAsync(ct);
        return issuer;
    }

    private static PersonClusterRefDto ToDto(PersonClusterRecord record) => new(
        record.Id,
        record.Did,
        record.CanonicalUsername,
        record.DisplayName,
        record.PrimaryAuthServerUrl,
        record.DisambiguationTag,
        BuildDisplayLabel(record.CanonicalUsername, record.DisambiguationTag, record.PrimaryAuthServerUrl));

    private static string BuildDisplayLabel(string username, string? tag, string primaryAuthServerUrl)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            try
            {
                var uri = new Uri(primaryAuthServerUrl);
                tag = uri.Host.Split('.')[0].Replace("auth-", "").Replace("localhost", "local");
            }
            catch
            {
                tag = string.Empty;
            }
        }

        return string.IsNullOrWhiteSpace(tag) ? username : $"{username} · {tag}";
    }
}
