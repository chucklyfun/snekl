using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;

namespace Snekl.Account.Infrastructure.Services;

public interface IForumNavLayoutService
{
    Task<ForumNavLayoutDto> GetAsync(Guid clusterId, CancellationToken ct = default);
    Task<ForumNavLayoutDto> SaveAsync(Guid clusterId, ForumNavLayoutDto layout, CancellationToken ct = default);
}

public class ForumNavLayoutService : IForumNavLayoutService
{
    private static readonly HashSet<string> AllowedHiddenSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "section:favorites",
        "section:recents",
        "section:manage",
        "section:joined"
    };

    private static readonly string[] AllowedAxes = ["section", "federation", "server"];
    private static readonly string[] DefaultAxes = ["section"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly AccountDbContext _db;

    public ForumNavLayoutService(AccountDbContext db) => _db = db;

    public async Task<ForumNavLayoutDto> GetAsync(Guid clusterId, CancellationToken ct = default)
    {
        await EnsureClusterExistsAsync(clusterId, ct);
        var settings = await GetOrCreateSettingsAsync(clusterId, ct);
        return Normalize(Deserialize(settings.ForumNavLayoutJson));
    }

    public async Task<ForumNavLayoutDto> SaveAsync(
        Guid clusterId,
        ForumNavLayoutDto layout,
        CancellationToken ct = default)
    {
        await EnsureClusterExistsAsync(clusterId, ct);
        var settings = await GetOrCreateSettingsAsync(clusterId, ct);
        var normalized = Normalize(layout);
        settings.ForumNavLayoutJson = JsonSerializer.Serialize(normalized, JsonOptions);
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return normalized;
    }

    internal static ForumNavLayoutDto Normalize(ForumNavLayoutDto? layout)
    {
        var folders = new List<ForumNavFolderDto>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in layout?.Folders ?? [])
        {
            var id = (folder.Id ?? "").Trim();
            var name = (folder.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;
            if (name.Length > 80) name = name[..80];
            if (!seenIds.Add(id)) continue;
            var parentId = string.IsNullOrWhiteSpace(folder.ParentId) ? null : folder.ParentId.Trim();
            folders.Add(new ForumNavFolderDto(id, name, parentId, folder.Position));
        }

        var ids = folders.Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        folders = folders
            .Select(f => ids.Contains(f.ParentId ?? "") ? f : f with { ParentId = null })
            .Where(f => !CreatesCycle(f, folders))
            .OrderBy(f => f.Position)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ids = folders.Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var items = new List<ForumNavFolderItemDto>();
        var seenItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in layout?.Items ?? [])
        {
            var folderId = (item.FolderId ?? "").Trim();
            var serverDid = (item.ServerDid ?? "").Trim();
            var forumId = (item.ForumId ?? "").Trim();
            var name = (item.Name ?? "").Trim();
            var slug = (item.Slug ?? "").Trim();
            if (!ids.Contains(folderId) || string.IsNullOrWhiteSpace(serverDid) || string.IsNullOrWhiteSpace(forumId))
                continue;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug)) continue;
            var key = $"{folderId}:{serverDid}:{forumId}";
            if (!seenItems.Add(key)) continue;
            items.Add(new ForumNavFolderItemDto(
                folderId,
                serverDid,
                forumId,
                name,
                slug,
                string.IsNullOrWhiteSpace(item.ParentSlug) ? null : item.ParentSlug.Trim(),
                string.IsNullOrWhiteSpace(item.ServerName) ? serverDid : item.ServerName.Trim(),
                item.Position));
        }

        var hidden = new List<ForumNavHiddenDto>();
        var seenHidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in layout?.Hidden ?? [])
        {
            var sectionId = (entry.SectionId ?? "").Trim().ToLowerInvariant();
            var serverDid = (entry.ServerDid ?? "").Trim();
            var forumId = (entry.ForumId ?? "").Trim();
            if (!AllowedHiddenSections.Contains(sectionId)
                || string.IsNullOrWhiteSpace(serverDid)
                || string.IsNullOrWhiteSpace(forumId))
                continue;
            var key = $"{sectionId}:{serverDid}:{forumId}";
            if (!seenHidden.Add(key)) continue;
            hidden.Add(new ForumNavHiddenDto(sectionId, serverDid, forumId));
        }

        var axes = new List<string>();
        var seenAxes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (layout?.GroupAxes is null)
            axes.AddRange(DefaultAxes);
        else
        {
            foreach (var axis in layout.GroupAxes)
            {
                var value = (axis ?? "").Trim().ToLowerInvariant();
                if (!AllowedAxes.Contains(value) || !seenAxes.Add(value)) continue;
                axes.Add(value);
            }
        }

        var folderPeerIds = ids.Select(id => $"folder:{id}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var peers = new List<ForumNavPeerDto>();
        var seenPeers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var peer in layout?.Peers ?? [])
        {
            var id = (peer.Id ?? "").Trim();
            if (string.IsNullOrWhiteSpace(id) || !seenPeers.Add(id)) continue;
            var isBuiltin = AllowedHiddenSections.Contains(id);
            var isFolder = folderPeerIds.Contains(id);
            if (!isBuiltin && !isFolder) continue;
            peers.Add(new ForumNavPeerDto(id, peer.Enabled, peer.Position, NormalizePeerAxes(peer.GroupAxes)));
        }

        return new ForumNavLayoutDto(
            folders,
            items.OrderBy(i => i.Position).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            hidden,
            axes,
            peers.OrderBy(p => p.Position).ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static IReadOnlyList<string>? NormalizePeerAxes(IReadOnlyList<string>? axes)
    {
        if (axes is null) return null;
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var axis in axes)
        {
            var value = (axis ?? "").Trim().ToLowerInvariant();
            if (value is not ("federation" or "server") || !seen.Add(value)) continue;
            result.Add(value);
        }
        return result;
    }

    private static bool CreatesCycle(ForumNavFolderDto folder, IReadOnlyList<ForumNavFolderDto> folders)
    {
        var byId = folders.ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = folder.ParentId;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (string.Equals(current, folder.Id, StringComparison.OrdinalIgnoreCase) || !seen.Add(current))
                return true;
            if (!byId.TryGetValue(current, out var parent))
                return false;
            current = parent.ParentId;
        }
        return false;
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

    private static ForumNavLayoutDto Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json is "{}" or "[]")
            return new ForumNavLayoutDto([], []);

        try
        {
            return JsonSerializer.Deserialize<ForumNavLayoutDto>(json, JsonOptions)
                ?? new ForumNavLayoutDto([], []);
        }
        catch
        {
            return new ForumNavLayoutDto([], []);
        }
    }
}
