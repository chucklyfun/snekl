using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface INestMapService
{
    Task<IReadOnlyList<NestMapNodeDto>> ListAsync(Guid userId, CancellationToken ct = default);
    Task PlaceAsync(Guid userId, Guid forumId, PlaceNestRequest request, CancellationToken ct = default);
    Task SetLockedAsync(Guid userId, Guid forumId, bool locked, CancellationToken ct = default);
    Task SetHiddenAsync(Guid userId, Guid forumId, bool hidden, CancellationToken ct = default);
    Task PurgeAsync(Guid userId, Guid forumId, CancellationToken ct = default);
    Task<NestRedirectDto?> RedirectForAsync(string slug, string? parentSlug, Guid? viewerUserId, CancellationToken ct = default);
}

public sealed class NestMapService : INestMapService
{
    private readonly SneklDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly IForumService _forums;
    private readonly ICurrentServer? _current;

    public NestMapService(
        SneklDbContext db,
        IPermissionService permissions,
        IForumService forums,
        ICurrentServer? current = null)
    {
        _db = db;
        _permissions = permissions;
        _forums = forums;
        _current = current;
    }

    public async Task<IReadOnlyList<NestMapNodeDto>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        await RequireOperatorAsync(userId, ct);
        var forums = await _db.Forums.AsNoTracking().ToListAsync(ct);
        return ToNodes(forums);
    }

    public async Task PlaceAsync(Guid userId, Guid forumId, PlaceNestRequest request, CancellationToken ct = default)
    {
        await RequireOperatorAsync(userId, ct);
        var forums = await _db.Forums.ToListAsync(ct);
        var byEntity = forums.ToDictionary(f => f.Id);
        if (!byEntity.TryGetValue(forumId, out var forum))
            throw new InvalidOperationException("Nest not found.");

        if (request.ParentForumId == forumId)
            throw new InvalidOperationException("A nest cannot be placed under itself.");

        if (request.ParentForumId is Guid parentId)
        {
            if (!byEntity.ContainsKey(parentId))
                throw new InvalidOperationException("Parent nest not found.");
            var parents = Parents(forums);
            if (NestTopology.BranchIds(forumId, parents).Contains(parentId))
                throw new InvalidOperationException("A nest cannot be placed under one of its own nests.");
        }

        var refs = Refs(forums);
        var byRef = refs.ToDictionary(r => r.Id);
        var oldAddresses = NestTopology.BranchIds(forumId, Parents(forums))
            .Select(id => (Id: id, Address: NestTopology.PublicAddress(id, byRef)))
            .ToList();

        forum.ParentForumId = request.ParentForumId;
        forum.UpdatedAt = DateTime.UtcNow;

        var moved = NestTopology.BranchIds(forumId, Parents(forums)).ToHashSet();
        var rootId = NestTopology.RootId(forumId, Parents(forums));
        if (forum.ParentForumId is null)
        {
            var rootSlugs = forums
                .Where(f => f.Id != forum.Id && f.ParentForumId is null)
                .Select(f => f.Slug)
                .ToHashSet(StringComparer.Ordinal);
            forum.Slug = NestTopology.NextSlug(forum.Slug, rootSlugs);
        }

        var taken = NestTopology.DescendantSlugs(rootId, RefsById(forums), moved);
        foreach (var id in moved)
        {
            if (id == rootId)
                continue;
            var node = byEntity[id];
            var slug = NestTopology.NextSlug(node.Slug, taken);
            node.Slug = slug;
            taken.Add(slug);
            node.UpdatedAt = DateTime.UtcNow;
        }

        var siblings = forums
            .Where(f => f.ParentForumId == request.ParentForumId && f.Id != forumId)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Name)
            .ToList();
        var index = Math.Clamp(request.SortOrder, 0, siblings.Count);
        siblings.Insert(index, forum);
        for (var i = 0; i < siblings.Count; i++)
            siblings[i].SortOrder = i;

        var serverId = forum.ServerId != Guid.Empty
            ? forum.ServerId
            : _current?.ServerId ?? Guid.Empty;
        var updatedRefs = Refs(forums).ToDictionary(r => r.Id);
        foreach (var (id, address) in oldAddresses)
        {
            var current = NestTopology.PublicAddress(id, updatedRefs);
            if (current == address)
                continue;
            if (serverId == Guid.Empty)
                continue;
            await NestRedirects.RememberAsync(_db, serverId, address.TopSlug, address.Slug, id, ct);
            await NestRedirects.ForgetAddressAsync(_db, serverId, current.TopSlug, current.Slug, ct);
        }

        await _db.SaveChangesAsync(ct);
    }

    public Task SetLockedAsync(Guid userId, Guid forumId, bool locked, CancellationToken ct = default) =>
        SetFlagAsync(userId, forumId, locked, hidden: null, ct);

    public Task SetHiddenAsync(Guid userId, Guid forumId, bool hidden, CancellationToken ct = default) =>
        SetFlagAsync(userId, forumId, locked: null, hidden, ct);

    public async Task PurgeAsync(Guid userId, Guid forumId, CancellationToken ct = default)
    {
        await RequireOperatorAsync(userId, ct);
        var forum = await _db.Forums.FirstOrDefaultAsync(f => f.Id == forumId, ct)
            ?? throw new InvalidOperationException("Nest not found.");
        if (!forum.Hidden)
            throw new InvalidOperationException("Hide the nest before purging it.");

        await _forums.DeleteAsync(userId, forumId, ct);
    }

    public async Task<NestRedirectDto?> RedirectForAsync(
        string slug,
        string? parentSlug,
        Guid? viewerUserId,
        CancellationToken ct = default)
    {
        var top = string.IsNullOrWhiteSpace(parentSlug) ? string.Empty : parentSlug.Trim().ToLowerInvariant();
        var normalized = slug.Trim().ToLowerInvariant();
        var row = await _db.ForumRedirects.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TopSlug == top && r.Slug == normalized, ct);
        if (row is null)
            return null;

        var target = await _db.Forums.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == row.TargetForumId, ct);
        if (target is null)
            return null;

        if (await IsConcealedAsync(target, ct) && !await CanSeeConcealedAsync(viewerUserId, target.ServerId, ct))
            return null;

        var onServer = await _db.Forums.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.ServerId == target.ServerId)
            .Select(f => new NestRef(f.Id, f.ParentForumId, f.Slug))
            .ToListAsync(ct);
        var byId = onServer.ToDictionary(n => n.Id);
        if (!byId.ContainsKey(target.Id))
            return null;

        var address = NestTopology.PublicAddress(target.Id, byId);
        var node = await _db.NodeConfig.AsNoTracking().FirstOrDefaultAsync(n => n.Id == target.ServerId, ct);
        if (node is null)
            return null;

        return new NestRedirectDto(node.Id, node.Did, address.Slug, address.TopSlug);
    }

    private async Task SetFlagAsync(
        Guid userId,
        Guid forumId,
        bool? locked,
        bool? hidden,
        CancellationToken ct)
    {
        await RequireOperatorAsync(userId, ct);
        var forums = await _db.Forums.ToListAsync(ct);
        var ids = NestTopology.BranchIds(forumId, Parents(forums));
        if (!forums.Any(f => f.Id == forumId))
            throw new InvalidOperationException("Nest not found.");

        var now = DateTime.UtcNow;
        foreach (var forum in forums.Where(f => ids.Contains(f.Id)))
        {
            if (locked is bool lockedValue)
                forum.Locked = lockedValue;
            if (hidden is bool hiddenValue)
                forum.Hidden = hiddenValue;
            forum.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task RequireOperatorAsync(Guid userId, CancellationToken ct)
    {
        if (await _permissions.IsSuperUserAsync(userId, ct) || await _permissions.IsServerOwnerAsync(userId, ct))
            return;
        throw new PermissionDeniedException("SuperUser", "server");
    }

    private async Task<bool> IsConcealedAsync(Forum forum, CancellationToken ct)
    {
        var guard = 0;
        var current = forum;
        while (guard++ < NestTopology.MaxDepth)
        {
            if (current.Locked || current.Hidden)
                return true;
            if (current.ParentForumId is not Guid parentId)
                return false;
            var parent = await _db.Forums.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == parentId, ct);
            if (parent is null)
                return false;
            current = parent;
        }

        return true;
    }

    private async Task<bool> CanSeeConcealedAsync(Guid? viewerUserId, Guid serverId, CancellationToken ct)
    {
        if (!viewerUserId.HasValue)
            return false;

        if (_current?.ServerId is Guid scoped && scoped == serverId)
            return await _permissions.IsSuperUserAsync(viewerUserId, ct)
                || await _permissions.IsServerOwnerAsync(viewerUserId, ct);

        var viewer = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == viewerUserId.Value, ct);
        if (viewer is null || string.IsNullOrWhiteSpace(viewer.Did))
            return false;

        var destUser = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(u => u.ServerId == serverId && u.Did == viewer.Did, ct);
        if (destUser is null)
            return false;

        if (await _db.SuperUserGrants.IgnoreQueryFilters().AnyAsync(g => g.ServerId == serverId && g.UserId == destUser.Id, ct))
            return true;

        return await _db.NodeConfig.AsNoTracking().AnyAsync(n => n.Id == serverId && n.OwnerUserId == destUser.Id, ct);
    }

    private static List<NestMapNodeDto> ToNodes(IReadOnlyList<Forum> forums)
    {
        var parents = Parents(forums);
        var byRef = Refs(forums).ToDictionary(r => r.Id);
        return forums
            .OrderBy(f => Depth(f.Id, parents))
            .ThenBy(f => f.SortOrder)
            .ThenBy(f => f.Name)
            .Select(f =>
            {
                var address = NestTopology.PublicAddress(f.Id, byRef);
                return new NestMapNodeDto(
                    f.Id,
                    f.Name,
                    f.Slug,
                    f.ParentForumId,
                    address.TopSlug,
                    f.SortOrder,
                    f.Locked,
                    f.Hidden,
                    Depth(f.Id, parents));
            })
            .ToList();
    }

    private static int Depth(Guid id, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        var depth = 0;
        while (parents.TryGetValue(id, out var parent) && parent is Guid next && depth < NestTopology.MaxDepth)
        {
            depth++;
            id = next;
        }

        return depth;
    }

    private static Dictionary<Guid, Guid?> Parents(IEnumerable<Forum> forums) =>
        forums.ToDictionary(f => f.Id, f => f.ParentForumId);

    private static List<NestRef> Refs(IEnumerable<Forum> forums) =>
        forums.Select(f => new NestRef(f.Id, f.ParentForumId, f.Slug)).ToList();

    private static Dictionary<Guid, NestRef> RefsById(IEnumerable<Forum> forums) =>
        Refs(forums).ToDictionary(r => r.Id);
}
