using Microsoft.EntityFrameworkCore;
using Snekl.Domain;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IWatercoolerService
{
    Task<WatercoolerDto> CreateAsync(Guid userId, CreateWatercoolerRequest request, Guid? viewerUserId = null, CancellationToken ct = default);
    Task<WatercoolerDto> ApproveAsync(Guid userId, Guid watercoolerId, CancellationToken ct = default);
    Task RejectAsync(Guid userId, Guid watercoolerId, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid watercoolerId, CancellationToken ct = default);
    Task<WatercoolerDto> UpdateAsync(Guid userId, Guid watercoolerId, UpdateWatercoolerRequest request, CancellationToken ct = default);
    Task<WatercoolerDto?> GetBySlugAsync(string slug, Guid? parentForumId, Guid? viewerUserId, CancellationToken ct = default);
    Task<Watercooler?> GetEntityBySlugAsync(string slug, Guid? parentForumId, CancellationToken ct = default);
    Task<IReadOnlyList<WatercoolerDto>> ListServerAsync(Guid? viewerUserId, CancellationToken ct = default);
    Task<IReadOnlyList<WatercoolerSummaryDto>> ListForForumAsync(Guid forumId, Guid? viewerUserId, CancellationToken ct = default);
    Task<IReadOnlyList<WatercoolerRoleDto>> ListRolesAsync(Guid watercoolerId, Guid userId, CancellationToken ct = default);
    Task<WatercoolerRoleDto> CreateRoleAsync(Guid watercoolerId, Guid userId, CreateWatercoolerRoleRequest request, CancellationToken ct = default);
    Task<WatercoolerRoleDto> UpdateRoleAsync(Guid watercoolerId, Guid roleId, Guid userId, UpdateWatercoolerRoleRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ForumContentBlockDto>> ListForumBlocksAsync(Guid forumId, Guid? viewerUserId, CancellationToken ct = default);
    Task<IReadOnlyList<ForumContentBlockDto>> ReplaceForumBlocksAsync(Guid forumId, Guid userId, UpsertForumContentBlocksRequest request, CancellationToken ct = default);
}

public class WatercoolerService : IWatercoolerService
{
    private readonly SneklDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly IRoleSeedService _roleSeed;
    private readonly IUserPresentationService _presentation;
    private readonly IChangeLogService? _changeLog;
    private readonly IForumPageService _forumPages;

    public WatercoolerService(
        SneklDbContext db,
        IPermissionService permissions,
        IRoleSeedService roleSeed,
        IUserPresentationService presentation,
        IForumPageService forumPages,
        IChangeLogService? changeLog = null)
    {
        _db = db;
        _permissions = permissions;
        _roleSeed = roleSeed;
        _presentation = presentation;
        _forumPages = forumPages;
        _changeLog = changeLog;
    }

    public async Task<WatercoolerDto> CreateAsync(
        Guid userId,
        CreateWatercoolerRequest request,
        Guid? viewerUserId = null,
        CancellationToken ct = default)
    {
        Forum? parent = null;
        var approvalStatus = ForumApprovalStatuses.Approved;
        if (request.ParentForumId.HasValue)
        {
            parent = await _db.Forums.FindAsync([request.ParentForumId.Value], ct)
                ?? throw new InvalidOperationException("Parent nest not found.");
            await _permissions.RequireForumPermissionAsync(userId, parent.Id, ForumPermissions.ManageForum, ct);
        }
        else if (!await _permissions.IsSuperUserAsync(userId, ct))
        {
            var modes = await _permissions.GetServerPermissionModesAsync(userId, ct);
            if (PermissionModeMap.AtLeast(modes, ServerPermissions.CreateWatercooler, GrantMode.Direct))
                approvalStatus = ForumApprovalStatuses.Approved;
            else if (PermissionModeMap.AtLeast(modes, ServerPermissions.CreateWatercooler, GrantMode.Request))
                approvalStatus = ForumApprovalStatuses.Pending;
            else
                throw new PermissionDeniedException(nameof(ServerPermissions.CreateWatercooler), "server");
        }

        var slug = request.Slug.Trim().ToLowerInvariant();
        var parentId = request.ParentForumId;
        if (await _db.Watercoolers.AnyAsync(w => w.ParentForumId == parentId && w.Slug == slug, ct))
            throw new InvalidOperationException("Watercooler slug already exists.");

        var cooler = new Watercooler
        {
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description.Trim(),
            CreatedByUserId = userId,
            ParentForumId = parentId,
            ApprovalStatus = approvalStatus,
            AllowUpvotes = request.AllowUpvotes ?? parentId is null,
            NewestAtBottom = request.NewestAtBottom ?? true
        };
        _db.Watercoolers.Add(cooler);
        await _db.SaveChangesAsync(ct);
        await _roleSeed.SeedWatercoolerRolesAsync(cooler.Id, userId, parentId, ct);

        return await ToDtoAsync(cooler, viewerUserId ?? userId, ct);
    }

    public async Task<WatercoolerDto> ApproveAsync(Guid userId, Guid watercoolerId, CancellationToken ct = default)
    {
        await RequireApprovePermissionAsync(userId, ct);
        var cooler = await LoadAsync(watercoolerId, ct);
        if (ForumApprovalStatuses.IsApproved(cooler.ApprovalStatus))
            throw new InvalidOperationException("Watercooler is already approved.");
        cooler.ApprovalStatus = ForumApprovalStatuses.Approved;
        cooler.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await ToDtoAsync(cooler, userId, ct);
    }

    public async Task RejectAsync(Guid userId, Guid watercoolerId, CancellationToken ct = default)
    {
        await RequireApprovePermissionAsync(userId, ct);
        var cooler = await LoadAsync(watercoolerId, ct);
        _db.Watercoolers.Remove(cooler);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid userId, Guid watercoolerId, CancellationToken ct = default)
    {
        var cooler = await LoadAsync(watercoolerId, ct);
        if (cooler.ParentForumId.HasValue)
            await _permissions.RequireForumPermissionAsync(userId, cooler.ParentForumId.Value, ForumPermissions.ManageForum, ct);
        else
            await _permissions.RequireWatercoolerPermissionAsync(userId, cooler.Id, WatercoolerPermissions.ManageWatercooler, ct);

        _db.Watercoolers.Remove(cooler);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<WatercoolerDto> UpdateAsync(
        Guid userId,
        Guid watercoolerId,
        UpdateWatercoolerRequest request,
        CancellationToken ct = default)
    {
        var cooler = await LoadAsync(watercoolerId, ct);
        await _permissions.RequireWatercoolerPermissionAsync(userId, cooler.Id, WatercoolerPermissions.ManageWatercooler, ct);
        var before = WatercoolerSnapshot(cooler);
        if (request.Name is not null) cooler.Name = request.Name.Trim();
        if (request.Description is not null) cooler.Description = request.Description.Trim();
        if (request.AllowUpvotes.HasValue) cooler.AllowUpvotes = request.AllowUpvotes.Value;
        if (request.NewestAtBottom.HasValue) cooler.NewestAtBottom = request.NewestAtBottom.Value;
        cooler.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        if (_changeLog is not null)
        {
            await _changeLog.RecordAsync(
                ChangeLogScopes.Watercooler,
                cooler.ParentForumId,
                cooler.Id,
                ChangeLogEntityTypes.Watercooler,
                cooler.Id,
                ChangeLogActions.Updated,
                userId,
                $"Updated watercooler {cooler.Name}",
                before,
                WatercoolerSnapshot(cooler),
                ct);
        }
        return await ToDtoAsync(cooler, userId, ct);
    }

    public async Task<WatercoolerDto?> GetBySlugAsync(
        string slug,
        Guid? parentForumId,
        Guid? viewerUserId,
        CancellationToken ct = default)
    {
        var cooler = await GetEntityBySlugAsync(slug, parentForumId, ct);
        if (cooler is null) return null;
        if (!await _permissions.CanSeeWatercoolerAsync(viewerUserId, cooler.Id, ct))
            return null;
        return await ToDtoAsync(cooler, viewerUserId, ct);
    }

    public async Task<Watercooler?> GetEntityBySlugAsync(string slug, Guid? parentForumId, CancellationToken ct = default)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        return await _db.Watercoolers
            .Include(w => w.ParentForum)
            .FirstOrDefaultAsync(w => w.Slug == normalized && w.ParentForumId == parentForumId, ct);
    }

    public async Task<IReadOnlyList<WatercoolerDto>> ListServerAsync(Guid? viewerUserId, CancellationToken ct = default)
    {
        var coolers = await _db.Watercoolers
            .Include(w => w.ParentForum)
            .Where(w => w.ParentForumId == null)
            .OrderBy(w => w.Name)
            .ToListAsync(ct);
        var result = new List<WatercoolerDto>();
        foreach (var cooler in coolers)
        {
            if (!await _permissions.CanSeeWatercoolerAsync(viewerUserId, cooler.Id, ct)) continue;
            result.Add(await ToDtoAsync(cooler, viewerUserId, ct));
        }

        return result;
    }

    public async Task<IReadOnlyList<WatercoolerSummaryDto>> ListForForumAsync(
        Guid forumId,
        Guid? viewerUserId,
        CancellationToken ct = default)
    {
        var coolers = await _db.Watercoolers
            .Where(w => w.ParentForumId == forumId)
            .OrderBy(w => w.Name)
            .ToListAsync(ct);
        var result = new List<WatercoolerSummaryDto>();
        foreach (var cooler in coolers)
        {
            if (!await _permissions.CanSeeWatercoolerAsync(viewerUserId, cooler.Id, ct)) continue;
            result.Add(await ToSummaryAsync(cooler, viewerUserId, ct));
        }

        return result;
    }

    public async Task<IReadOnlyList<WatercoolerRoleDto>> ListRolesAsync(
        Guid watercoolerId,
        Guid userId,
        CancellationToken ct = default)
    {
        await _permissions.RequireWatercoolerPermissionAsync(userId, watercoolerId, WatercoolerPermissions.ManageWatercooler, ct);
        var roles = await _db.WatercoolerRoles
            .Where(r => r.WatercoolerId == watercoolerId)
            .OrderBy(r => r.Position)
            .ToListAsync(ct);
        return roles.Select(ToRoleDto).ToList();
    }

    public async Task<WatercoolerRoleDto> CreateRoleAsync(
        Guid watercoolerId,
        Guid userId,
        CreateWatercoolerRoleRequest request,
        CancellationToken ct = default)
    {
        await _permissions.RequireWatercoolerPermissionAsync(userId, watercoolerId, WatercoolerPermissions.ManageWatercooler, ct);
        if (!await _db.Watercoolers.AnyAsync(w => w.Id == watercoolerId, ct))
            throw new InvalidOperationException("Watercooler not found.");
        var role = new WatercoolerRole
        {
            WatercoolerId = watercoolerId,
            Name = request.Name.Trim(),
            Color = request.Color,
            Position = request.Position,
            Modes = PermissionParsing.ParseWatercoolerModes(request.Permissions)
        };
        _db.WatercoolerRoles.Add(role);
        await _db.SaveChangesAsync(ct);
        if (_changeLog is not null)
        {
            await _changeLog.RecordAsync(
                ChangeLogScopes.Watercooler,
                null,
                watercoolerId,
                ChangeLogEntityTypes.WatercoolerRole,
                role.Id,
                ChangeLogActions.Created,
                userId,
                $"Created watercooler role {role.Name}",
                null,
                WatercoolerRoleSnapshot(role),
                ct);
        }
        return ToRoleDto(role);
    }

    public async Task<WatercoolerRoleDto> UpdateRoleAsync(
        Guid watercoolerId,
        Guid roleId,
        Guid userId,
        UpdateWatercoolerRoleRequest request,
        CancellationToken ct = default)
    {
        await _permissions.RequireWatercoolerPermissionAsync(userId, watercoolerId, WatercoolerPermissions.ManageWatercooler, ct);
        var role = await _db.WatercoolerRoles.FirstOrDefaultAsync(r => r.Id == roleId && r.WatercoolerId == watercoolerId, ct)
            ?? throw new InvalidOperationException("Role not found.");
        var before = WatercoolerRoleSnapshot(role);
        if (!role.IsEveryone)
            role.Name = request.Name.Trim();
        role.Color = request.Color;
        role.Position = request.Position;
        role.Modes = PermissionParsing.ParseWatercoolerModes(request.Permissions);
        await _db.SaveChangesAsync(ct);
        if (_changeLog is not null)
        {
            await _changeLog.RecordAsync(
                ChangeLogScopes.Watercooler,
                null,
                watercoolerId,
                ChangeLogEntityTypes.WatercoolerRole,
                role.Id,
                ChangeLogActions.Updated,
                userId,
                $"Updated watercooler role {role.Name}",
                before,
                WatercoolerRoleSnapshot(role),
                ct);
        }
        return ToRoleDto(role);
    }

    public async Task<IReadOnlyList<ForumContentBlockDto>> ListForumBlocksAsync(
        Guid forumId,
        Guid? viewerUserId,
        CancellationToken ct = default)
    {
        if (!await _permissions.CanSeeForumAsync(viewerUserId, forumId, ct))
            return [];
        var blocks = await _db.ForumContentBlocks
            .Where(b => b.ForumId == forumId)
            .OrderBy(b => b.Slot)
            .ThenBy(b => b.Position)
            .ToListAsync(ct);
        return blocks.Select(ToBlockDto).ToList();
    }

    public async Task<IReadOnlyList<ForumContentBlockDto>> ReplaceForumBlocksAsync(
        Guid forumId,
        Guid userId,
        UpsertForumContentBlocksRequest request,
        CancellationToken ct = default)
    {
        await _permissions.RequireForumPermissionAsync(userId, forumId, ForumPermissions.ManageContentBlocks, ct);
        var existing = await _db.ForumContentBlocks.Where(b => b.ForumId == forumId).ToListAsync(ct);
        _db.ForumContentBlocks.RemoveRange(existing);
        var position = 0;
        foreach (var block in request.Blocks)
        {
            if (block.TypeKey is not WatercoolerBlockTypes.CoolerList and not WatercoolerBlockTypes.CoolerFeed)
                throw new InvalidOperationException("Unknown content block type.");
            if (block.TypeKey == WatercoolerBlockTypes.CoolerFeed && !block.WatercoolerId.HasValue)
                throw new InvalidOperationException("Cooler feed blocks require a watercooler.");
            _db.ForumContentBlocks.Add(new ForumContentBlock
            {
                ForumId = forumId,
                TypeKey = block.TypeKey,
                WatercoolerId = block.WatercoolerId,
                Slot = string.IsNullOrWhiteSpace(block.Slot) ? ContentBlockSlots.Main : block.Slot,
                Position = position++
            });
        }

        await _db.SaveChangesAsync(ct);
        var pinned = request.Blocks
            .FirstOrDefault(b => b.TypeKey == WatercoolerBlockTypes.CoolerFeed && b.WatercoolerId.HasValue);
        await _forumPages.SyncPinnedWatercoolerAsync(forumId, pinned?.WatercoolerId, ct);
        return await ListForumBlocksAsync(forumId, userId, ct);
    }

    private async Task RequireApprovePermissionAsync(Guid userId, CancellationToken ct)
    {
        if (await _permissions.IsSuperUserAsync(userId, ct)) return;
        await _permissions.RequireServerPermissionAsync(userId, ServerPermissions.CreateWatercooler, ct);
    }

    private async Task<Watercooler> LoadAsync(Guid id, CancellationToken ct) =>
        await _db.Watercoolers.Include(w => w.ParentForum).FirstOrDefaultAsync(w => w.Id == id, ct)
        ?? throw new InvalidOperationException("Watercooler not found.");

    private async Task<WatercoolerSummaryDto> ToSummaryAsync(Watercooler cooler, Guid? viewerUserId, CancellationToken ct)
    {
        var count = await _db.Posts.CountAsync(p => p.WatercoolerId == cooler.Id && p.ParentPostId == null, ct);
        var canView = await _permissions.CanViewWatercoolerAsync(viewerUserId, cooler.Id, ct);
        var canSpeak = await _permissions.CanSpeakWatercoolerAsync(viewerUserId, cooler.Id, ct);
        return new WatercoolerSummaryDto(
            cooler.Id,
            cooler.Name,
            cooler.Slug,
            count,
            cooler.AllowUpvotes,
            cooler.NewestAtBottom,
            cooler.ApprovalStatus,
            canView,
            canSpeak);
    }

    private async Task<WatercoolerDto> ToDtoAsync(Watercooler cooler, Guid? viewerUserId, CancellationToken ct)
    {
        var count = await _db.Posts.CountAsync(p => p.WatercoolerId == cooler.Id && p.ParentPostId == null, ct);
        var createdBy = cooler.CreatedByUserId != Guid.Empty
            ? await _presentation.GetPresentationAsync(cooler.CreatedByUserId, viewerUserId, cooler.ParentForumId, ct)
            : null;
        IReadOnlyList<string>? viewerPerms = null;
        var canManage = false;
        var canApprove = false;
        if (viewerUserId.HasValue)
        {
            var modes = await _permissions.GetWatercoolerPermissionModesAsync(viewerUserId, cooler.Id, ct);
            viewerPerms = PermissionNames.ToNames(modes, server: false);
            canManage = PermissionModeMap.AtLeast(modes, WatercoolerPermissions.ManageWatercooler, GrantMode.Direct)
                || PermissionModeMap.AtLeast(modes, WatercoolerPermissions.WatercoolerAdministrator, GrantMode.Direct);
            if (!ForumApprovalStatuses.IsApproved(cooler.ApprovalStatus))
            {
                canApprove = await _permissions.IsSuperUserAsync(viewerUserId, ct)
                    || PermissionModeMap.AtLeast(
                        await _permissions.GetServerPermissionModesAsync(viewerUserId, ct),
                        ServerPermissions.CreateWatercooler,
                        GrantMode.Direct);
            }
        }

        return new WatercoolerDto(
            cooler.Id,
            cooler.Name,
            cooler.Slug,
            cooler.Description,
            createdBy,
            cooler.ParentForumId,
            cooler.ParentForum?.Slug,
            cooler.ParentForum?.Name,
            cooler.AllowUpvotes,
            cooler.NewestAtBottom,
            cooler.ApprovalStatus,
            cooler.CreatedAt,
            count,
            viewerPerms,
            canApprove,
            canManage);
    }

    private static WatercoolerRoleDto ToRoleDto(WatercoolerRole role) =>
        new(role.Id, role.Name, role.Color, role.Position, PermissionNames.ToNames(role.Modes, server: false), role.IsEveryone);

    private static object WatercoolerSnapshot(Watercooler cooler) => new
    {
        name = cooler.Name,
        description = cooler.Description,
        allowUpvotes = cooler.AllowUpvotes,
        newestAtBottom = cooler.NewestAtBottom
    };

    private static object WatercoolerRoleSnapshot(WatercoolerRole role) => new
    {
        name = role.Name,
        color = role.Color,
        position = role.Position,
        permissions = PermissionNames.ToNames(role.Modes, server: false)
    };

    private static ForumContentBlockDto ToBlockDto(ForumContentBlock block) =>
        new(block.Id, block.TypeKey, block.WatercoolerId, block.Slot, block.Position);
}
