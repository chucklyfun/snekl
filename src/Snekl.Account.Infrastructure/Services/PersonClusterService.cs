using Microsoft.EntityFrameworkCore;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;
using Snekl.Account.Infrastructure.Security;

namespace Snekl.Account.Infrastructure.Services;

public static class PersonClusterMapper
{
    public static PersonClusterDto ToDto(PersonCluster cluster, IEnumerable<AuthIdentity> identities) => new(
        cluster.Id,
        cluster.Did,
        cluster.CanonicalUsername,
        cluster.DisplayName,
        cluster.Email,
        cluster.Bio,
        cluster.PrimaryAuthServerUrl,
        cluster.DisambiguationTag,
        VouchSigner.BuildDisplayLabel(cluster.CanonicalUsername, cluster.DisambiguationTag, cluster.PrimaryAuthServerUrl),
        identities.Select(i => new AuthIdentityDto(i.Id, i.AuthServerUrl, i.IsPrimary, i.PasswordHash is not null)).ToList());
}

public interface IPersonClusterService
{
    Task<PersonClusterDto?> GetAsync(Guid clusterId, CancellationToken ct = default);
    Task<PersonClusterDto> UpdateProfileAsync(Guid clusterId, UpdateProfileRequest request, CancellationToken ct = default);
}

public class PersonClusterService : IPersonClusterService
{
    private readonly AccountDbContext _db;

    public PersonClusterService(AccountDbContext db) => _db = db;

    public async Task<PersonClusterDto?> GetAsync(Guid clusterId, CancellationToken ct = default)
    {
        var cluster = await _db.PersonClusters
            .Include(c => c.Identities)
            .FirstOrDefaultAsync(c => c.Id == clusterId, ct);
        return cluster is null ? null : PersonClusterMapper.ToDto(cluster, cluster.Identities);
    }

    public async Task<PersonClusterDto> UpdateProfileAsync(Guid clusterId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var cluster = await _db.PersonClusters.Include(c => c.Identities).FirstOrDefaultAsync(c => c.Id == clusterId, ct)
            ?? throw new InvalidOperationException("Person cluster not found.");

        cluster.DisplayName = request.DisplayName.Trim();
        cluster.Bio = request.Bio.Trim();
        cluster.DisambiguationTag = string.IsNullOrWhiteSpace(request.DisambiguationTag) ? null : request.DisambiguationTag.Trim();
        cluster.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return PersonClusterMapper.ToDto(cluster, cluster.Identities);
    }
}
