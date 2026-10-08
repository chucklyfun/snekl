using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;

namespace Snekl.Account.Infrastructure.Services;

public interface IForumFavoriteService
{
    Task<IReadOnlyList<ForumFavoriteDto>> ListAsync(Guid clusterId, CancellationToken ct = default);
    Task<ForumFavoriteDto> UpsertAsync(Guid clusterId, UpsertForumFavoriteRequest request, CancellationToken ct = default);
    Task RemoveAsync(Guid clusterId, string serverDid, string forumId, CancellationToken ct = default);
}

public class ForumFavoriteService : IForumFavoriteService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly AccountDbContext _db;

    public ForumFavoriteService(AccountDbContext db) => _db = db;

    public async Task<IReadOnlyList<ForumFavoriteDto>> ListAsync(Guid clusterId, CancellationToken ct = default)
    {
        await EnsureClusterExistsAsync(clusterId, ct);
        var settings = await GetOrCreateSettingsAsync(clusterId, ct);
        return Deserialize(settings.ForumFavoritesJson)
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ForumFavoriteDto> UpsertAsync(
        Guid clusterId,
        UpsertForumFavoriteRequest request,
        CancellationToken ct = default)
    {
        await EnsureClusterExistsAsync(clusterId, ct);
        var serverDid = (request.ServerDid ?? "").Trim();
        var forumId = (request.ForumId ?? "").Trim();
        var name = (request.Name ?? "").Trim();
        var slug = (request.Slug ?? "").Trim();
        if (string.IsNullOrWhiteSpace(serverDid) || string.IsNullOrWhiteSpace(forumId))
            throw new InvalidOperationException("Server DID and nest id are required.");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug))
            throw new InvalidOperationException("Nest name and slug are required.");

        var settings = await GetOrCreateSettingsAsync(clusterId, ct);
        var favorites = Deserialize(settings.ForumFavoritesJson).ToList();
        var index = favorites.FindIndex(f =>
            string.Equals(f.ServerDid, serverDid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(f.ForumId, forumId, StringComparison.OrdinalIgnoreCase));

        var favorite = new ForumFavoriteDto(
            serverDid,
            forumId,
            name,
            slug,
            string.IsNullOrWhiteSpace(request.ParentSlug) ? null : request.ParentSlug.Trim(),
            string.IsNullOrWhiteSpace(request.ServerName) ? serverDid : request.ServerName.Trim(),
            index >= 0 ? favorites[index].AddedAt : DateTime.UtcNow);

        if (index >= 0)
            favorites[index] = favorite;
        else
            favorites.Add(favorite);

        await SaveAsync(settings, favorites, ct);
        return favorite;
    }

    public async Task RemoveAsync(Guid clusterId, string serverDid, string forumId, CancellationToken ct = default)
    {
        await EnsureClusterExistsAsync(clusterId, ct);
        var settings = await GetOrCreateSettingsAsync(clusterId, ct);
        var favorites = Deserialize(settings.ForumFavoritesJson).ToList();
        var removed = favorites.RemoveAll(f =>
            string.Equals(f.ServerDid, serverDid.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(f.ForumId, forumId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            throw new InvalidOperationException("Favorite not found.");

        await SaveAsync(settings, favorites, ct);
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
        IReadOnlyList<ForumFavoriteDto> favorites,
        CancellationToken ct)
    {
        settings.ForumFavoritesJson = JsonSerializer.Serialize(favorites, JsonOptions);
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static List<ForumFavoriteDto> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<ForumFavoriteDto>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
