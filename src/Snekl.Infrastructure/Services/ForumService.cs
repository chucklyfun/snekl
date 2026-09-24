using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IForumService
{
    Task<ForumDto> CreateAsync(Guid userId, CreateForumRequest request, Guid? viewerUserId = null, CancellationToken ct = default);
    Task<ForumDto> ApproveAsync(Guid userId, Guid forumId, CancellationToken ct = default);
    Task RejectAsync(Guid userId, Guid forumId, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid forumId, CancellationToken ct = default);
    Task<IReadOnlyList<ForumDto>> ListAsync(Guid? viewerUserId = null, CancellationToken ct = default);
    Task<ForumDto?> GetBySlugAsync(string slug, Guid? viewerUserId = null, string? parentSlug = null, CancellationToken ct = default);
    Task<Forum?> GetEntityBySlugAsync(string slug, string? parentSlug = null, CancellationToken ct = default);
    Task<ForumDto> JoinAsync(Guid userId, string slug, string? parentSlug = null, CancellationToken ct = default);
    Task<ForumDto> LeaveAsync(Guid userId, string slug, string? parentSlug = null, CancellationToken ct = default);
    Task<ForumDto> SetFavoriteAsync(Guid userId, string slug, bool isFavorite, string? parentSlug = null, CancellationToken ct = default);
    Task UpsertFederatedAsync(FederatedForumPayload payload, Guid sourceNodeId, string sourceNodeUrl, CancellationToken ct = default);
}

public class ForumService : IForumService
{
    private readonly SneklDbContext _db;
    private readonly IUserPresentationService _presentation;
    private readonly IPermissionService _permissions;
    private readonly IRoleSeedService _roleSeed;
    private readonly IForumModerationService _moderation;
    private readonly IHubFeedPublisher _hubFeed;
    private readonly INsfwAgeCheckService _nsfwAge;
    private readonly IMediaService _media;

    public ForumService(
        SneklDbContext db,
        IUserPresentationService presentation,
        IPermissionService permissions,
        IRoleSeedService roleSeed,
        IForumModerationService moderation,
        IHubFeedPublisher hubFeed,
        INsfwAgeCheckService nsfwAge,
        IMediaService media)
    {
        _db = db;
        _presentation = presentation;
        _permissions = permissions;
        _roleSeed = roleSeed;
        _moderation = moderation;
        _hubFeed = hubFeed;
        _nsfwAge = nsfwAge;
        _media = media;
    }

    public async Task<ForumDto> CreateAsync(Guid userId, CreateForumRequest request, Guid? viewerUserId = null, CancellationToken ct = default)
    {
        Forum? parent = null;
        var approvalStatus = ForumApprovalStatuses.Approved;
        if (request.ParentForumId.HasValue)
        {
            parent = await _db.Forums.FindAsync([request.ParentForumId.Value], ct)
                ?? throw new InvalidOperationException("Parent forum not found.");
            await _permissions.RequireForumPermissionAsync(userId, parent.Id, ForumPermissions.ManageForum, ct);
        }
        else
        {
            if (await _permissions.IsSuperUserAsync(userId, ct))
                approvalStatus = ForumApprovalStatuses.Approved;
            else
            {
                var serverPerms = await _permissions.GetServerPermissionsAsync(userId, ct);
                if (serverPerms.HasFlag(ServerPermissions.CreateForum))
                    approvalStatus = ForumApprovalStatuses.Approved;
                else if (serverPerms.HasFlag(ServerPermissions.RequestCreateForum))
                    approvalStatus = ForumApprovalStatuses.Pending;
                else
                    throw new PermissionDeniedException(nameof(ServerPermissions.CreateForum), "server");
            }
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
            AllowAliases = parent?.AllowAliases ?? true,
            ApprovalStatus = approvalStatus
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

    public async Task<ForumDto> ApproveAsync(Guid userId, Guid forumId, CancellationToken ct = default)
    {
        await RequireForumApprovalPermissionAsync(userId, ct);
        var forum = await _db.Forums
            .Include(f => f.CreatedBy)
            .Include(f => f.ParentForum)
            .Include(f => f.Subforums)
            .FirstOrDefaultAsync(f => f.Id == forumId, ct)
            ?? throw new InvalidOperationException("Forum not found.");

        if (ForumApprovalStatuses.IsApproved(forum.ApprovalStatus))
            throw new InvalidOperationException("Forum is already approved.");

        forum.ApprovalStatus = ForumApprovalStatuses.Approved;
        forum.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var createdBy = forum.CreatedByUserId != Guid.Empty
            ? await _presentation.GetPresentationAsync(forum.CreatedByUserId, userId, forum.Id, ct)
            : null;
        return await ToDtoAsync(forum, createdBy, 0, userId, ct);
    }

    public async Task RejectAsync(Guid userId, Guid forumId, CancellationToken ct = default)
    {
        await RequireForumApprovalPermissionAsync(userId, ct);
        var forum = await _db.Forums.FirstOrDefaultAsync(f => f.Id == forumId, ct)
            ?? throw new InvalidOperationException("Forum not found.");

        if (ForumApprovalStatuses.IsApproved(forum.ApprovalStatus))
            throw new InvalidOperationException("Approved forums cannot be rejected. Delete the forum instead.");

        var roleIds = await _db.ForumRoles.Where(r => r.ForumId == forum.Id).Select(r => r.Id).ToListAsync(ct);
        _db.ForumMemberships.RemoveRange(
            await _db.ForumMemberships.Where(m => m.ForumId == forum.Id).ToListAsync(ct));
        _db.ForumRoleAssignments.RemoveRange(
            await _db.ForumRoleAssignments.Where(a => a.ForumId == forum.Id).ToListAsync(ct));
        _db.ForumFlairTemplates.RemoveRange(
            await _db.ForumFlairTemplates.Where(t => t.ForumId == forum.Id).ToListAsync(ct));
        _db.ForumRoles.RemoveRange(await _db.ForumRoles.Where(r => roleIds.Contains(r.Id)).ToListAsync(ct));
        _db.Forums.Remove(forum);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid userId, Guid forumId, CancellationToken ct = default)
    {
        var forum = await _db.Forums.FirstOrDefaultAsync(f => f.Id == forumId, ct)
            ?? throw new InvalidOperationException("Forum not found.");

        if (forum.ParentForumId.HasValue)
            await _permissions.RequireForumPermissionAsync(userId, forum.ParentForumId.Value, ForumPermissions.ManageForum, ct);
        else
            await RequireForumDeletePermissionAsync(userId, ct);

        var childIds = await _db.Forums.Where(f => f.ParentForumId == forum.Id).Select(f => f.Id).ToListAsync(ct);
        foreach (var childId in childIds)
            await DeleteAsync(userId, childId, ct);

        forum = await _db.Forums.FirstOrDefaultAsync(f => f.Id == forumId, ct)
            ?? throw new InvalidOperationException("Forum not found.");
        await RemoveForumGraphAsync(forum, ct);
    }

    private async Task RequireForumDeletePermissionAsync(Guid userId, CancellationToken ct)
    {
        if (await _permissions.IsSuperUserAsync(userId, ct)) return;
        await _permissions.RequireServerPermissionAsync(userId, ServerPermissions.CreateForum, ct);
    }

    private async Task RemoveForumGraphAsync(Forum forum, CancellationToken ct)
    {
        var postIds = await _db.Posts.Where(p => p.ForumId == forum.Id).Select(p => p.Id).ToListAsync(ct);
        if (postIds.Count > 0)
        {
            _db.PostMedia.RemoveRange(await _db.PostMedia.Where(m => postIds.Contains(m.PostId)).ToListAsync(ct));
            _db.PostUpvotes.RemoveRange(await _db.PostUpvotes.Where(u => postIds.Contains(u.PostId)).ToListAsync(ct));
            _db.PostRevisions.RemoveRange(await _db.PostRevisions.Where(r => postIds.Contains(r.PostId)).ToListAsync(ct));
            _db.PostReports.RemoveRange(await _db.PostReports.Where(r => postIds.Contains(r.PostId)).ToListAsync(ct));
            _db.Posts.RemoveRange(await _db.Posts.Where(p => p.ForumId == forum.Id).ToListAsync(ct));
        }

        _db.ForumMemberFlairs.RemoveRange(
            await _db.ForumMemberFlairs.Where(f => f.ForumId == forum.Id).ToListAsync(ct));
        _db.ForumMemberAliases.RemoveRange(
            await _db.ForumMemberAliases.Where(a => a.ForumId == forum.Id).ToListAsync(ct));
        _db.ForumMemberBans.RemoveRange(
            await _db.ForumMemberBans.Where(b => b.ForumId == forum.Id).ToListAsync(ct));
        _db.PostReports.RemoveRange(
            await _db.PostReports.Where(r => r.ForumId == forum.Id).ToListAsync(ct));
        _db.ForumMemberships.RemoveRange(
            await _db.ForumMemberships.Where(m => m.ForumId == forum.Id).ToListAsync(ct));
        _db.ForumRoleAssignments.RemoveRange(
            await _db.ForumRoleAssignments.Where(a => a.ForumId == forum.Id).ToListAsync(ct));
        _db.ForumFlairTemplates.RemoveRange(
            await _db.ForumFlairTemplates.Where(t => t.ForumId == forum.Id).ToListAsync(ct));
        _db.ForumRoles.RemoveRange(await _db.ForumRoles.Where(r => r.ForumId == forum.Id).ToListAsync(ct));
        _db.Forums.Remove(forum);
        await _db.SaveChangesAsync(ct);
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

    public async Task<ForumDto> JoinAsync(Guid userId, string slug, string? parentSlug = null, CancellationToken ct = default)
    {
        var forum = await GetEntityBySlugAsync(slug, parentSlug, ct)
            ?? throw new InvalidOperationException("Forum not found.");

        await EnsureForumRolesAsync(forum, ct);

        if (!ForumApprovalStatuses.IsApproved(forum.ApprovalStatus))
            throw new InvalidOperationException("This forum is waiting for approval and cannot be joined yet.");

        if (!await _permissions.CanViewForumAsync(userId, forum.Id, ct))
            throw new PermissionDeniedException(ForumPermissions.ViewForum.ToString(), "forum");

        await _nsfwAge.EnsureViewerMayEnterForumAsync(userId, forum.Id, ct);

        if (await _moderation.IsBannedAsync(forum.Id, userId, ct))
            throw new InvalidOperationException("You are banned from this forum.");

        if (!await _permissions.HasForumRoleAssignmentAsync(userId, forum.Id, ct))
        {
            var member = await _roleSeed.EnsureMemberRoleAsync(forum.Id, ct);
            _db.ForumRoleAssignments.Add(new ForumRoleAssignment
            {
                ForumId = forum.Id,
                UserId = userId,
                RoleId = member.Id
            });
        }

        await EnsureMembershipAsync(userId, forum.Id, ct);
        await _db.SaveChangesAsync(ct);
        await PublishMembershipAsync(userId, forum.Id, deleted: false, ct);

        return await ToViewerDtoAsync(forum, userId, ct);
    }

    public async Task<ForumDto> LeaveAsync(Guid userId, string slug, string? parentSlug = null, CancellationToken ct = default)
    {
        var forum = await GetEntityBySlugAsync(slug, parentSlug, ct)
            ?? throw new InvalidOperationException("Forum not found.");

        if (!await _permissions.HasForumRoleAssignmentAsync(userId, forum.Id, ct)
            && !await _db.ForumMemberships.AnyAsync(m => m.ForumId == forum.Id && m.UserId == userId, ct))
            return await ToViewerDtoAsync(forum, userId, ct);

        if (await _permissions.HasForumRoleAssignmentAsync(userId, forum.Id, ct))
            await EnsureNotLastAdministratorAsync(forum.Id, userId, ct);

        var existing = await _db.ForumRoleAssignments
            .Where(a => a.ForumId == forum.Id && a.UserId == userId)
            .ToListAsync(ct);
        _db.ForumRoleAssignments.RemoveRange(existing);
        _db.ForumMemberships.RemoveRange(
            await _db.ForumMemberships.Where(m => m.ForumId == forum.Id && m.UserId == userId).ToListAsync(ct));
        await _db.SaveChangesAsync(ct);
        await PublishMembershipAsync(userId, forum.Id, deleted: true, ct);

        return await ToViewerDtoAsync(forum, userId, ct);
    }

    public async Task<ForumDto> SetFavoriteAsync(
        Guid userId,
        string slug,
        bool isFavorite,
        string? parentSlug = null,
        CancellationToken ct = default)
    {
        var forum = await GetEntityBySlugAsync(slug, parentSlug, ct)
            ?? throw new InvalidOperationException("Forum not found.");

        if (!await _permissions.HasForumRoleAssignmentAsync(userId, forum.Id, ct)
            && !await _db.ForumMemberships.AnyAsync(m => m.ForumId == forum.Id && m.UserId == userId, ct))
            throw new InvalidOperationException("Join this forum before adding it to favorites.");

        var membership = await EnsureMembershipAsync(userId, forum.Id, ct);
        membership.IsFavorite = isFavorite;
        membership.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await PublishMembershipAsync(userId, forum.Id, deleted: false, ct);
        return await ToViewerDtoAsync(forum, userId, ct);
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
            subforumSummaries.Add(new ForumSummaryDto(
                sub.Id, sub.Name, sub.Slug, subCount,
                sub.AvatarMediaAssetId is Guid avatarId ? _media.BuildPublicUrl(avatarId) : null));
        }

        IReadOnlyList<string>? viewerPerms = null;
        var viewerIsMember = false;
        var viewerCanManage = false;
        var viewerIsFavorite = false;
        if (viewerUserId.HasValue)
        {
            var perms = await _permissions.GetForumPermissionsAsync(viewerUserId, forum.Id, ct);
            viewerPerms = PermissionNames.ToNames(perms);
            var membership = await _db.ForumMemberships.AsNoTracking()
                .FirstOrDefaultAsync(m => m.ForumId == forum.Id && m.UserId == viewerUserId.Value, ct);
            viewerIsMember = membership is not null
                || await _permissions.HasForumRoleAssignmentAsync(viewerUserId, forum.Id, ct);
            viewerIsFavorite = membership?.IsFavorite == true;
            viewerCanManage = await _permissions.ViewerCanManageFromRolesAsync(viewerUserId, forum.Id, ct);
        }

        var watercoolers = new List<WatercoolerSummaryDto>();
        var coolerRows = await _db.Watercoolers
            .Where(w => w.ParentForumId == forum.Id)
            .OrderBy(w => w.Name)
            .ToListAsync(ct);
        foreach (var cooler in coolerRows)
        {
            if (!await _permissions.CanSeeWatercoolerAsync(viewerUserId, cooler.Id, ct)) continue;
            var coolerCount = await _db.Posts.CountAsync(p => p.WatercoolerId == cooler.Id && p.ParentPostId == null, ct);
            var canViewCooler = await _permissions.CanViewWatercoolerAsync(viewerUserId, cooler.Id, ct);
            var canSpeakCooler = await _permissions.CanSpeakWatercoolerAsync(viewerUserId, cooler.Id, ct);
            watercoolers.Add(new WatercoolerSummaryDto(
                cooler.Id,
                cooler.Name,
                cooler.Slug,
                coolerCount,
                cooler.AllowUpvotes,
                cooler.NewestAtBottom,
                cooler.ApprovalStatus,
                canViewCooler,
                canSpeakCooler));
        }

        var contentBlocks = await _db.ForumContentBlocks
            .Where(b => b.ForumId == forum.Id)
            .OrderBy(b => b.Slot)
            .ThenBy(b => b.Position)
            .Select(b => new ForumContentBlockDto(b.Id, b.TypeKey, b.WatercoolerId, b.Slot, b.Position))
            .ToListAsync(ct);

        var age = await _nsfwAge.GetStatusAsync(viewerUserId, forum.Id, ct);
        if (age.BlocksEntry)
        {
            subforumSummaries.Clear();
            watercoolers.Clear();
            contentBlocks.Clear();
        }

        string? Avatar(Guid? id) => id is Guid mediaId ? _media.BuildPublicUrl(mediaId) : null;

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
            forum.AllowAliases,
            string.IsNullOrWhiteSpace(forum.ApprovalStatus) ? ForumApprovalStatuses.Approved : forum.ApprovalStatus,
            await ViewerCanApproveAsync(viewerUserId, forum, ct),
            viewerIsMember,
            viewerCanManage,
            viewerIsFavorite,
            watercoolers,
            contentBlocks,
            forum.NsfwAgeCheckRequired,
            age.RequiredByServer,
            age.Enforced,
            age.MinimumAge,
            age.ViewerAttested,
            Avatar(forum.AvatarMediaAssetId),
            Avatar(forum.BannerMediaAssetId),
            forum.NsfwAgeCheckExplanation,
            age.BlocksEntry,
            age.ForumExplanation);
    }

    private async Task RequireForumApprovalPermissionAsync(Guid userId, CancellationToken ct)
    {
        if (await _permissions.IsSuperUserAsync(userId, ct)) return;
        await _permissions.RequireServerPermissionAsync(userId, ServerPermissions.CreateForum, ct);
    }

    private async Task<bool> ViewerCanApproveAsync(Guid? viewerUserId, Forum forum, CancellationToken ct)
    {
        if (!viewerUserId.HasValue || ForumApprovalStatuses.IsApproved(forum.ApprovalStatus))
            return false;
        if (await _permissions.IsSuperUserAsync(viewerUserId, ct)) return true;
        var perms = await _permissions.GetServerPermissionsAsync(viewerUserId, ct);
        return perms.HasFlag(ServerPermissions.CreateForum);
    }

    private async Task EnsureForumRolesAsync(Forum forum, CancellationToken ct)
    {
        if (await _db.ForumRoles.AnyAsync(r => r.ForumId == forum.Id, ct))
            return;

        var creatorId = forum.CreatedByUserId;
        if (creatorId == Guid.Empty)
        {
            var anyUser = await _db.Users.AsNoTracking().Select(u => u.Id).FirstOrDefaultAsync(ct);
            if (anyUser == Guid.Empty) return;
            creatorId = anyUser;
        }

        await _roleSeed.SeedForumRolesAsync(forum.Id, creatorId, ct);
        if (forum.OwnerUserId is null)
        {
            forum.OwnerUserId = creatorId;
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task<ForumDto> ToViewerDtoAsync(Forum forum, Guid viewerUserId, CancellationToken ct)
    {
        var postCount = await _db.Posts.CountAsync(p => p.ForumId == forum.Id && p.ParentPostId == null, ct);
        var createdBy = forum.CreatedByUserId != Guid.Empty
            ? await _presentation.GetPresentationAsync(forum.CreatedByUserId, viewerUserId, forum.Id, ct)
            : null;
        return await ToDtoAsync(forum, createdBy, postCount, viewerUserId, ct);
    }

    private async Task EnsureNotLastAdministratorAsync(Guid forumId, Guid userId, CancellationToken ct)
    {
        var roles = await _db.ForumRoles
            .Where(r => r.ForumId == forumId && !r.IsEveryone)
            .ToListAsync(ct);
        var adminRoleIds = roles
            .Where(r => PermissionModeMap.Get(r.Modes, ForumPermissions.ForumAdministrator) == GrantMode.Direct)
            .Select(r => r.Id)
            .ToHashSet();
        if (adminRoleIds.Count == 0)
            return;

        var adminUserIds = await _db.ForumRoleAssignments
            .Where(a => a.ForumId == forumId && adminRoleIds.Contains(a.RoleId))
            .Select(a => a.UserId)
            .Distinct()
            .ToListAsync(ct);

        if (adminUserIds.Count == 1 && adminUserIds[0] == userId)
            throw new InvalidOperationException("Cannot leave this forum as the last owner.");
    }

    private async Task<ForumMembership> EnsureMembershipAsync(Guid userId, Guid forumId, CancellationToken ct)
    {
        var existing = await _db.ForumMemberships.FirstOrDefaultAsync(
            m => m.ForumId == forumId && m.UserId == userId,
            ct);
        if (existing is not null) return existing;

        var created = new ForumMembership
        {
            ForumId = forumId,
            UserId = userId,
            IsFavorite = false,
            UpdatedAt = DateTime.UtcNow
        };
        _db.ForumMemberships.Add(created);
        return created;
    }

    private async Task PublishMembershipAsync(Guid userId, Guid forumId, bool deleted, CancellationToken ct)
    {
        var did = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Did)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(did))
            return;
        var isFavorite = !deleted && await _db.ForumMemberships.AsNoTracking()
            .Where(m => m.ForumId == forumId && m.UserId == userId)
            .Select(m => m.IsFavorite)
            .FirstOrDefaultAsync(ct);
        await _hubFeed.PublishForumMembershipAsync(did, forumId, deleted, isFavorite, ct);
    }
}
