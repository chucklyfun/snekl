using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IForumService
{
    Task<ForumDto> CreateAsync(Guid userId, CreateForumRequest request, Guid? viewerUserId = null, CancellationToken ct = default);
    Task<IReadOnlyList<ForumDto>> ListAsync(Guid? viewerUserId = null, CancellationToken ct = default);
    Task<ForumDto?> GetBySlugAsync(string slug, Guid? viewerUserId = null, string? parentSlug = null, CancellationToken ct = default);
    Task<Forum?> GetEntityBySlugAsync(string slug, string? parentSlug = null, CancellationToken ct = default);
    Task UpsertFederatedAsync(FederatedForumPayload payload, Guid sourceNodeId, string sourceNodeUrl, CancellationToken ct = default);
}

public class ForumService : IForumService
{
    private readonly SneklDbContext _db;
    private readonly IUserPresentationService _presentation;
    private readonly IPermissionService _permissions;
    private readonly IRoleSeedService _roleSeed;

    public ForumService(
        SneklDbContext db,
        IUserPresentationService presentation,
        IPermissionService permissions,
        IRoleSeedService roleSeed)
    {
        _db = db;
        _presentation = presentation;
        _permissions = permissions;
        _roleSeed = roleSeed;
    }

    public async Task<ForumDto> CreateAsync(Guid userId, CreateForumRequest request, Guid? viewerUserId = null, CancellationToken ct = default)
    {
        Forum? parent = null;
        if (request.ParentForumId.HasValue)
        {
            parent = await _db.Forums.FindAsync([request.ParentForumId.Value], ct)
                ?? throw new InvalidOperationException("Parent forum not found.");
            await _permissions.RequireForumPermissionAsync(userId, parent.Id, ForumPermissions.ManageForum, ct);
        }
        else
        {
            await _permissions.RequireServerPermissionAsync(userId, ServerPermissions.CreateForum, ct);
        }

        var slug = request.Slug.Trim().ToLowerInvariant();
        var parentId = request.ParentForumId;
        if (await _db.Forums.AnyAsync(f => f.ParentForumId == parentId && f.Slug == slug, ct))
            throw new InvalidOperationException("Forum slug already exists.");

        var forum = new Forum
        {
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description.Trim(),
            CreatedByUserId = userId,
            ParentForumId = parentId,
            AllowAliases = parent?.AllowAliases ?? true
        };

        _db.Forums.Add(forum);
        await _db.SaveChangesAsync(ct);

        if (parent is not null)
            await _roleSeed.InheritForumRolesFromParentAsync(forum.Id, parent.Id, ct);
        else
            await _roleSeed.SeedForumRolesAsync(forum.Id, userId, ct);

        var createdBy = await _presentation.GetPresentationAsync(userId, viewerUserId ?? userId, forum.Id, ct);
        return await ToDtoAsync(forum, createdBy, 0, viewerUserId, ct);
    }

    public async Task<IReadOnlyList<ForumDto>> ListAsync(Guid? viewerUserId = null, CancellationToken ct = default)
    {
        if (viewerUserId is null)
        {
            var serverPerms = await _permissions.GetServerPermissionsAsync(null, ct);
            if (!serverPerms.HasFlag(ServerPermissions.ViewServer))
                return [];
        }

        var forums = await _db.Forums
            .Include(f => f.CreatedBy)
            .Include(f => f.ParentForum)
            .Include(f => f.Subforums)
            .Where(f => f.ParentForumId == null)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);

        var postCounts = await _db.Posts
            .Where(p => p.ParentPostId == null)
            .GroupBy(p => p.ForumId)
            .Select(g => new { ForumId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ForumId, x => x.Count, ct);

        var creatorIds = forums.Select(f => f.CreatedByUserId).Distinct();
        var presentations = await _presentation.GetPresentationsAsync(creatorIds, viewerUserId, null, ct);

        var result = new List<ForumDto>();
        foreach (var forum in forums)
        {
            if (!await _permissions.CanViewForumAsync(viewerUserId, forum.Id, ct))
                continue;

            var dto = await ToDtoAsync(
                forum,
                presentations.GetValueOrDefault(forum.CreatedByUserId),
                postCounts.GetValueOrDefault(forum.Id),
                viewerUserId,
                ct);
            result.Add(dto);
        }

        return result;
    }

    public async Task<ForumDto?> GetBySlugAsync(string slug, Guid? viewerUserId = null, string? parentSlug = null, CancellationToken ct = default)
    {
        var forum = await GetEntityBySlugAsync(slug, parentSlug, ct);
        if (forum is null) return null;

        if (!await _permissions.CanViewForumAsync(viewerUserId, forum.Id, ct))
            throw new PermissionDeniedException(ForumPermissions.ViewForum.ToString(), "forum");

        var postCount = await _db.Posts.CountAsync(p => p.ForumId == forum.Id && p.ParentPostId == null, ct);
        var createdBy = forum.CreatedByUserId != Guid.Empty
            ? await _presentation.GetPresentationAsync(forum.CreatedByUserId, viewerUserId, forum.Id, ct)
            : null;
        return await ToDtoAsync(forum, createdBy, postCount, viewerUserId, ct);
    }

    public async Task<Forum?> GetEntityBySlugAsync(string slug, string? parentSlug = null, CancellationToken ct = default)
    {
        var normalized = slug.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(parentSlug))
        {
            return await _db.Forums
                .Include(f => f.CreatedBy)
                .Include(f => f.ParentForum)
                .Include(f => f.Subforums)
                .FirstOrDefaultAsync(f => f.Slug == normalized && f.ParentForumId == null, ct);
        }

        var parent = await _db.Forums.FirstOrDefaultAsync(
            f => f.Slug == parentSlug.ToLowerInvariant() && f.ParentForumId == null, ct);
        if (parent is null) return null;

        return await _db.Forums
            .Include(f => f.CreatedBy)
            .Include(f => f.ParentForum)
            .Include(f => f.Subforums)
            .FirstOrDefaultAsync(f => f.Slug == normalized && f.ParentForumId == parent.Id, ct);
    }

    public async Task UpsertFederatedAsync(FederatedForumPayload payload, Guid sourceNodeId, string sourceNodeUrl, CancellationToken ct = default)
    {
        var existing = await _db.Forums.FirstOrDefaultAsync(f => f.Id == payload.Id, ct);
        if (existing is not null) return;

        var author = await _db.Users.FirstOrDefaultAsync(u => u.Id == payload.CreatedBy.Id, ct);
        if (author is null)
        {
            author = new User
            {
                Id = payload.CreatedBy.Id,
                Username = payload.CreatedBy.Username,
                DisplayName = payload.CreatedBy.DisplayName,
                Bio = payload.CreatedBy.Bio,
                OriginNodeId = sourceNodeId,
                OriginNodeUrl = payload.CreatedBy.OriginNodeUrl
            };
            _db.Users.Add(author);
        }

        var forum = new Forum
        {
            Id = payload.Id,
            Name = payload.Name,
            Slug = payload.Slug,
            Description = payload.Description,
            CreatedByUserId = author.Id,
            CreatedAt = payload.CreatedAt,
            UpdatedAt = payload.CreatedAt,
            OriginNodeId = sourceNodeId,
            OriginNodeUrl = sourceNodeUrl
        };

        _db.Forums.Add(forum);
        await _db.SaveChangesAsync(ct);
        await _roleSeed.SeedForumRolesAsync(forum.Id, author.Id, ct);
    }

    private async Task<ForumDto> ToDtoAsync(
        Forum forum,
        UserPresentationDto? createdBy,
        int postCount,
        Guid? viewerUserId,
        CancellationToken ct)
    {
        var subforumSummaries = new List<ForumSummaryDto>();
        foreach (var sub in forum.Subforums.OrderBy(s => s.Name))
        {
            if (!await _permissions.CanViewForumAsync(viewerUserId, sub.Id, ct))
                continue;
            var subCount = await _db.Posts.CountAsync(p => p.ForumId == sub.Id && p.ParentPostId == null, ct);
            subforumSummaries.Add(new ForumSummaryDto(sub.Id, sub.Name, sub.Slug, subCount));
        }

        IReadOnlyList<string>? viewerPerms = null;
        if (viewerUserId.HasValue)
        {
            var perms = await _permissions.GetForumPermissionsAsync(viewerUserId, forum.Id, ct);
            viewerPerms = PermissionNames.ToNames(perms);
        }

        return new ForumDto(
            forum.Id,
            forum.Name,
            forum.Slug,
            forum.Description,
            createdBy,
            forum.OriginNodeUrl,
            forum.IsFederated,
            postCount,
            forum.CreatedAt,
            forum.ParentForumId,
            forum.ParentForum?.Slug,
            subforumSummaries,
            viewerPerms,
            forum.AllowAliases);
    }
}
