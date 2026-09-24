using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;
using Snekl.Domain.Identity.Did;

namespace Snekl.Account.Infrastructure.Services;

public interface IFollowService
{
    Task<IReadOnlyList<FollowDto>> ListAsync(Guid clusterId, CancellationToken ct = default);
    Task<FollowDto> UpsertAsync(Guid clusterId, UpsertFollowRequest request, CancellationToken ct = default);
    Task RemoveAsync(Guid clusterId, string did, CancellationToken ct = default);
    Task<bool> FollowsAsync(string followerDid, string targetDid, CancellationToken ct = default);
}

public class FollowService : IFollowService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly AccountDbContext _db;

    public FollowService(AccountDbContext db) => _db = db;

    public async Task<IReadOnlyList<FollowDto>> ListAsync(Guid clusterId, CancellationToken ct = default)
    {
        await EnsureClusterExistsAsync(clusterId, ct);
        var settings = await GetOrCreateSettingsAsync(clusterId, ct);
        return Deserialize(settings.FollowsJson)
            .OrderBy(f => f.DisplayName ?? f.Username ?? f.Did, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<FollowDto> UpsertAsync(
        Guid clusterId,
        UpsertFollowRequest request,
        CancellationToken ct = default)
    {
        var person = await _db.PersonClusters.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clusterId, ct)
            ?? throw new UnauthorizedAccessException("Session expired. Please log in again.");

        var did = (request.Did ?? "").Trim();
        if (!DidKey.TryParse(did, out _))
            throw new InvalidOperationException("A valid did:key is required.");
        if (string.Equals(person.Did, did, StringComparison.Ordinal))
            throw new InvalidOperationException("You cannot follow yourself.");

        var settings = await GetOrCreateSettingsAsync(clusterId, ct);
        var follows = Deserialize(settings.FollowsJson).ToList();
        var index = follows.FindIndex(f => string.Equals(f.Did, did, StringComparison.Ordinal));

        var follow = new FollowDto(
            did,
            string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
            string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim(),
            index >= 0 ? follows[index].AddedAt : DateTime.UtcNow);

        if (index >= 0)
            follows[index] = follow;
        else
            follows.Add(follow);

        await SaveAsync(settings, follows, ct);
        return follow;
    }

    public async Task<bool> FollowsAsync(string followerDid, string targetDid, CancellationToken ct = default)
    {
        var follower = (followerDid ?? "").Trim();
        var target = (targetDid ?? "").Trim();
        if (follower.Length == 0 || target.Length == 0)
            return false;

        var cluster = await _db.PersonClusters.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Did == follower, ct);
        if (cluster is null)
            return false;

        var settings = await _db.SettingsReplicas.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PersonClusterId == cluster.Id, ct);
        if (settings is null)
            return false;

        return Deserialize(settings.FollowsJson)
            .Any(f => string.Equals(f.Did, target, StringComparison.Ordinal));
    }

    public async Task RemoveAsync(Guid clusterId, string did, CancellationToken ct = default)
    {
        await EnsureClusterExistsAsync(clusterId, ct);
        var settings = await GetOrCreateSettingsAsync(clusterId, ct);
        var follows = Deserialize(settings.FollowsJson).ToList();
        var removed = follows.RemoveAll(f => string.Equals(f.Did, did.Trim(), StringComparison.Ordinal));
        if (removed == 0)
            throw new InvalidOperationException("Follow not found.");

        await SaveAsync(settings, follows, ct);
    }

    private async Task EnsureClusterExistsAsync(Guid clusterId, CancellationToken ct)
    {
        if (!await _db.PersonClusters.AnyAsync(c => c.Id == clusterId, ct))
            throw new UnauthorizedAccessException("Session expired. Please log in again.");
    }

    private async Task<UserSettingsReplica> GetOrCreateSettingsAsync(Guid clusterId, CancellationToken ct)
    {
        var settings = await _db.SettingsReplicas.FirstOrDefaultAsync(s => s.PersonClusterId == clusterId, ct);
        if (settings is not null) return settings;

        settings = new UserSettingsReplica { PersonClusterId = clusterId };
        _db.SettingsReplicas.Add(settings);
        try
        {
            await _db.SaveChangesAsync(ct);
            return settings;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            settings = await _db.SettingsReplicas.FirstOrDefaultAsync(s => s.PersonClusterId == clusterId, ct);
            if (settings is not null) return settings;
            throw new InvalidOperationException("Failed to load account settings.");
        }
    }

    private async Task SaveAsync(
        UserSettingsReplica settings,
        IReadOnlyList<FollowDto> follows,
        CancellationToken ct)
    {
        settings.FollowsJson = JsonSerializer.Serialize(follows, JsonOptions);
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static List<FollowDto> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<FollowDto>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
