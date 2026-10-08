using Microsoft.EntityFrameworkCore;
using Snekl.Domain;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IPermissionService
{
    Task<bool> IsSuperUserAsync(Guid? userId, CancellationToken ct = default);
    Task<bool> IsServerOwnerAsync(Guid? userId, CancellationToken ct = default);
    Task<bool> CanOverrideForumOwnerAsync(Guid? userId, CancellationToken ct = default);
    Task<Dictionary<string, GrantMode>> GetServerPermissionModesAsync(Guid? userId, CancellationToken ct = default);
    Task<Dictionary<string, GrantMode>> GetForumPermissionModesAsync(Guid? userId, Guid forumId, CancellationToken ct = default);
    Task<ServerPermissions> GetServerPermissionsAsync(Guid? userId, CancellationToken ct = default);
    Task<ForumPermissions> GetForumPermissionsAsync(Guid? userId, Guid forumId, CancellationToken ct = default);
    Task RequireServerPermissionAsync(Guid userId, ServerPermissions flag, CancellationToken ct = default);
    Task RequireForumPermissionAsync(Guid userId, Guid forumId, ForumPermissions flag, CancellationToken ct = default);
    Task EnsureBotMayAttachMediaAsync(Guid userId, CancellationToken ct = default);
    Task<bool> CanSeeForumAsync(Guid? userId, Guid forumId, CancellationToken ct = default);
    Task<bool> CanViewForumAsync(Guid? userId, Guid forumId, CancellationToken ct = default);
    Task<bool> HasForumRoleAssignmentAsync(Guid? userId, Guid forumId, CancellationToken ct = default);
    Task<bool> ViewerCanManageFromRolesAsync(Guid? userId, Guid forumId, CancellationToken ct = default);
    Task<Dictionary<string, GrantMode>> GetWatercoolerPermissionModesAsync(Guid? userId, Guid watercoolerId, CancellationToken ct = default);
    Task<WatercoolerPermissions> GetWatercoolerPermissionsAsync(Guid? userId, Guid watercoolerId, CancellationToken ct = default);
    Task RequireWatercoolerPermissionAsync(Guid userId, Guid watercoolerId, WatercoolerPermissions flag, CancellationToken ct = default);
    Task<bool> CanSeeWatercoolerAsync(Guid? userId, Guid watercoolerId, CancellationToken ct = default);
    Task<bool> CanViewWatercoolerAsync(Guid? userId, Guid watercoolerId, CancellationToken ct = default);
    Task<bool> CanSpeakWatercoolerAsync(Guid? userId, Guid watercoolerId, CancellationToken ct = default);
}

public class PermissionService : IPermissionService
{
    private readonly SneklDbContext _db;
    private readonly INsfwAgeCheckService? _nsfwAge;
    private readonly ICurrentServer? _current;

    public PermissionService(SneklDbContext db, INsfwAgeCheckService? nsfwAge = null, ICurrentServer? current = null)
    {
        _db = db;
        _nsfwAge = nsfwAge;
        _current = current;
    }

    public async Task<bool> IsSuperUserAsync(Guid? userId, CancellationToken ct = default)
    {
        if (!userId.HasValue) return false;
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
        if (user is { IsBot: true }) return false;
        return await _db.SuperUserGrants.AnyAsync(g => g.UserId == userId.Value, ct);
    }

    public async Task<bool> IsServerOwnerAsync(Guid? userId, CancellationToken ct = default)
    {
        if (!userId.HasValue) return false;
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
        if (user is { IsBot: true }) return false;
        var nodes = _db.NodeConfig.AsNoTracking().Where(n => n.OwnerUserId == userId.Value);
        if (_current?.ServerId is Guid serverId && serverId != Guid.Empty)
            nodes = nodes.Where(n => n.Id == serverId);
        return await nodes.AnyAsync(ct);
    }

    private async Task<bool> IsNestOperatorAsync(Guid? userId, CancellationToken ct) =>
        await IsSuperUserAsync(userId, ct) || await IsServerOwnerAsync(userId, ct);

    public async Task<bool> CanOverrideForumOwnerAsync(Guid? userId, CancellationToken ct = default)
    {
        if (!userId.HasValue) return false;
        if (await IsServerOwnerAsync(userId, ct) || await IsSuperUserAsync(userId, ct))
            return true;
        var perms = await GetServerPermissionsAsync(userId, ct);
        return perms.HasFlag(ServerPermissions.ServerAdministrator);
    }

    public async Task<Dictionary<string, GrantMode>> GetServerPermissionModesAsync(
        Guid? userId,
        CancellationToken ct = default)
    {
        var botInstall = await TryGetBotInstallAsync(userId, ct);
        if (botInstall is not null)
            return ExpandServerModes(PermissionModeMap.FromServerFlags(botInstall.ServerPermissions));

        if (userId.HasValue && (await IsServerOwnerAsync(userId, ct) || await IsSuperUserAsync(userId, ct)))
        {
            var god = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(ServerPermissions.ServerAdministrator)] = GrantMode.Direct
            };
            return ExpandServerModes(god);
        }

        var everyone = await _db.ServerRoles.AsNoTracking().FirstOrDefaultAsync(r => r.IsEveryone, ct);
        var modes = CloneModes(everyone?.Modes);

        if (!userId.HasValue) return ExpandServerModes(modes);

        var roleIds = await _db.ServerRoleAssignments
            .Where(a => a.UserId == userId.Value)
            .Select(a => a.RoleId)
            .ToListAsync(ct);

        if (roleIds.Count == 0) return ExpandServerModes(modes);

        var roles = await _db.ServerRoles
            .AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .ToListAsync(ct);

        return ExpandServerModes(MergeRoleModes(everyone?.Modes, roles.Select(r => r.Modes)));
    }

    public async Task<Dictionary<string, GrantMode>> GetForumPermissionModesAsync(
        Guid? userId,
        Guid forumId,
        CancellationToken ct = default)
    {
        var botInstall = await TryGetBotInstallAsync(userId, ct);
        if (botInstall is not null)
        {
            if (botInstall.ResourceType == ExtensionResourceTypes.Forum && botInstall.ForumId != forumId)
                return await ClipViewNsfwAsync(
                    userId,
                    forumId,
                    ExpandForumModes(new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase)),
                    ct);
            return await ClipViewNsfwAsync(
                userId,
                forumId,
                ExpandForumModes(PermissionModeMap.FromForumFlags(botInstall.ForumPermissions)),
                ct);
        }

        if (userId.HasValue && (await IsServerOwnerAsync(userId, ct) || await IsSuperUserAsync(userId, ct)))
        {
            var god = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(ForumPermissions.ForumAdministrator)] = GrantMode.Direct
            };
            return await ClipViewNsfwAsync(userId, forumId, ExpandForumModes(god), ct);
        }

        var serverModes = await GetServerPermissionModesAsync(userId, ct);
        var forumModes = await GetForumOnlyPermissionModesAsync(userId, forumId, ct);
        return await ClipViewNsfwAsync(userId, forumId, MergeServerIntoForumModes(serverModes, forumModes), ct);
    }

    public async Task<ServerPermissions> GetServerPermissionsAsync(Guid? userId, CancellationToken ct = default) =>
        PermissionModeMap.ToServerFlags(await GetServerPermissionModesAsync(userId, ct));

    public async Task<ForumPermissions> GetForumPermissionsAsync(Guid? userId, Guid forumId, CancellationToken ct = default) =>
        PermissionModeMap.ToForumFlags(await GetForumPermissionModesAsync(userId, forumId, ct));

    public async Task RequireServerPermissionAsync(Guid userId, ServerPermissions flag, CancellationToken ct = default)
    {
        if (await IsServerOwnerAsync(userId, ct) || await IsSuperUserAsync(userId, ct)) return;

        var modes = await GetServerPermissionModesAsync(userId, ct);
        if (flag == ServerPermissions.RequestCreateForum)
        {
            if (!PermissionModeMap.AtLeast(modes, ServerPermissions.CreateForum, GrantMode.Request))
                throw new PermissionDeniedException(flag.ToString(), "server");
            return;
        }

        if (!PermissionModeMap.AtLeast(modes, flag, GrantMode.Direct))
            throw new PermissionDeniedException(flag.ToString(), "server");
    }

    public async Task RequireForumPermissionAsync(Guid userId, Guid forumId, ForumPermissions flag, CancellationToken ct = default)
    {
        if (await IsSuperUserAsync(userId, ct)) return;

        var modes = await GetForumPermissionModesAsync(userId, forumId, ct);
        if (!PermissionModeMap.AtLeast(modes, flag, GrantMode.Direct))
            throw new PermissionDeniedException(flag.ToString(), "forum");
    }

    public async Task EnsureBotMayAttachMediaAsync(Guid userId, CancellationToken ct = default)
    {
        var install = await TryGetBotInstallAsync(userId, ct);
        if (install is null) return;

        var server = await GetServerPermissionsAsync(userId, ct);
        if (server.HasFlag(ServerPermissions.CreateForum))
            return;

        var forumId = install.ForumId ?? Guid.Empty;
        var forum = await GetForumPermissionsAsync(userId, forumId, ct);
        if (forum.HasFlag(ForumPermissions.CreateThread)
            || forum.HasFlag(ForumPermissions.Reply)
            || forum.HasFlag(ForumPermissions.EditOwnPost)
            || forum.HasFlag(ForumPermissions.ManagePosts))
            return;

        throw new PermissionDeniedException(nameof(ForumPermissions.CreateThread), "forum");
    }

    public async Task<bool> CanSeeForumAsync(Guid? userId, Guid forumId, CancellationToken ct = default)
    {
        var forum = await _db.Forums.AsNoTracking().FirstOrDefaultAsync(f => f.Id == forumId, ct);
        if (forum is null) return false;

        if (forum.Locked || forum.Hidden)
            return await IsNestOperatorAsync(userId, ct);

        if (!await CanAccessPendingForumAsync(userId, forum, ct))
            return false;

        if (userId.HasValue && await IsSuperUserAsync(userId, ct))
            return true;

        if (forum.ParentForumId.HasValue && !await CanSeeForumAsync(userId, forum.ParentForumId.Value, ct))
            return false;

        var modes = await GetForumPermissionModesAsync(userId, forumId, ct);
        return PermissionModeMap.AtLeast(modes, ForumPermissions.SeeForum, GrantMode.Direct);
    }

    public async Task<bool> CanViewForumAsync(Guid? userId, Guid forumId, CancellationToken ct = default)
    {
        var forum = await _db.Forums.AsNoTracking().FirstOrDefaultAsync(f => f.Id == forumId, ct);
        if (forum is null) return false;

        if (forum.Locked || forum.Hidden)
            return await IsNestOperatorAsync(userId, ct);

        if (!await CanAccessPendingForumAsync(userId, forum, ct))
            return false;

        if (userId.HasValue && await IsSuperUserAsync(userId, ct))
            return true;

        if (!await CanSeeForumAsync(userId, forumId, ct))
            return false;

        if (forum.ParentForumId.HasValue && !await CanViewForumAsync(userId, forum.ParentForumId.Value, ct))
            return false;

        var modes = await GetForumPermissionModesAsync(userId, forumId, ct);
        return PermissionModeMap.AtLeast(modes, ForumPermissions.ViewForum, GrantMode.Direct);
    }

    public async Task<bool> HasForumRoleAssignmentAsync(
        Guid? userId,
        Guid forumId,
        CancellationToken ct = default)
    {
        if (!userId.HasValue) return false;
        return await _db.ForumRoleAssignments.AnyAsync(
            a => a.ForumId == forumId && a.UserId == userId.Value,
            ct);
    }

    public async Task<bool> ViewerCanManageFromRolesAsync(
        Guid? userId,
        Guid forumId,
        CancellationToken ct = default)
    {
        if (!userId.HasValue) return false;

        var modes = await GetForumOnlyPermissionModesAsync(userId, forumId, ct);
        return PermissionModeMap.AtLeast(modes, ForumPermissions.ManageForum, GrantMode.Direct)
            || PermissionModeMap.AtLeast(modes, ForumPermissions.ForumAdministrator, GrantMode.Direct)
            || PermissionModeMap.AtLeast(modes, ForumPermissions.ManageRoles, GrantMode.Direct)
            || PermissionModeMap.AtLeast(modes, ForumPermissions.ManageMembers, GrantMode.Direct);
    }

    public async Task<Dictionary<string, GrantMode>> GetWatercoolerPermissionModesAsync(
        Guid? userId,
        Guid watercoolerId,
        CancellationToken ct = default)
    {
        if (userId.HasValue && await IsSuperUserAsync(userId, ct))
        {
            var god = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(WatercoolerPermissions.WatercoolerAdministrator)] = GrantMode.Direct
            };
            return ExpandWatercoolerModes(god);
        }

        var everyone = await _db.WatercoolerRoles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.WatercoolerId == watercoolerId && r.IsEveryone, ct);
        var modes = CloneModes(everyone?.Modes);

        if (userId.HasValue)
        {
            var roleIds = await _db.WatercoolerRoleAssignments
                .Where(a => a.WatercoolerId == watercoolerId && a.UserId == userId.Value)
                .Select(a => a.RoleId)
                .ToListAsync(ct);
            if (roleIds.Count > 0)
            {
                var roles = await _db.WatercoolerRoles
                    .AsNoTracking()
                    .Where(r => roleIds.Contains(r.Id))
                    .ToListAsync(ct);
                modes = MergeRoleModes(everyone?.Modes, roles.Select(r => r.Modes));
            }
        }

        return ExpandWatercoolerModes(modes);
    }

    public async Task<WatercoolerPermissions> GetWatercoolerPermissionsAsync(
        Guid? userId,
        Guid watercoolerId,
        CancellationToken ct = default) =>
        PermissionModeMap.ToWatercoolerFlags(await GetWatercoolerPermissionModesAsync(userId, watercoolerId, ct));

    public async Task RequireWatercoolerPermissionAsync(
        Guid userId,
        Guid watercoolerId,
        WatercoolerPermissions flag,
        CancellationToken ct = default)
    {
        if (await IsSuperUserAsync(userId, ct)) return;
        var modes = await GetWatercoolerPermissionModesAsync(userId, watercoolerId, ct);
        if (!PermissionModeMap.AtLeast(modes, flag, GrantMode.Direct))
            throw new PermissionDeniedException(flag.ToString(), "watercooler");
    }

    public async Task<bool> CanSeeWatercoolerAsync(Guid? userId, Guid watercoolerId, CancellationToken ct = default)
    {
        var cooler = await _db.Watercoolers.AsNoTracking().FirstOrDefaultAsync(w => w.Id == watercoolerId, ct);
        if (cooler is null) return false;

        if (!ForumApprovalStatuses.IsApproved(cooler.ApprovalStatus))
        {
            if (!userId.HasValue) return false;
            if (cooler.CreatedByUserId != userId.Value && !await IsSuperUserAsync(userId, ct))
            {
                var serverModes = await GetServerPermissionModesAsync(userId, ct);
                if (!PermissionModeMap.AtLeast(serverModes, ServerPermissions.CreateWatercooler, GrantMode.Direct))
                    return false;
            }
        }

        if (userId.HasValue && await IsSuperUserAsync(userId, ct))
            return true;

        if (cooler.ParentForumId.HasValue && !await CanSeeForumAsync(userId, cooler.ParentForumId.Value, ct))
            return false;

        var modes = await GetWatercoolerPermissionModesAsync(userId, watercoolerId, ct);
        return PermissionModeMap.AtLeast(modes, WatercoolerPermissions.SeeWatercooler, GrantMode.Direct);
    }

    public async Task<bool> CanViewWatercoolerAsync(Guid? userId, Guid watercoolerId, CancellationToken ct = default)
    {
        if (!await CanSeeWatercoolerAsync(userId, watercoolerId, ct))
            return false;

        var cooler = await _db.Watercoolers.AsNoTracking().FirstOrDefaultAsync(w => w.Id == watercoolerId, ct);
        if (cooler is null) return false;

        if (userId.HasValue && await IsSuperUserAsync(userId, ct))
            return true;

        if (cooler.ParentForumId.HasValue && !await CanViewForumAsync(userId, cooler.ParentForumId.Value, ct))
            return false;

        var modes = await GetWatercoolerPermissionModesAsync(userId, watercoolerId, ct);
        return PermissionModeMap.AtLeast(modes, WatercoolerPermissions.ViewWatercooler, GrantMode.Direct);
    }

    public async Task<bool> CanSpeakWatercoolerAsync(Guid? userId, Guid watercoolerId, CancellationToken ct = default)
    {
        if (!userId.HasValue) return false;
        if (!await CanViewWatercoolerAsync(userId, watercoolerId, ct))
            return false;
        if (await IsSuperUserAsync(userId, ct))
            return true;
        var modes = await GetWatercoolerPermissionModesAsync(userId, watercoolerId, ct);
        return PermissionModeMap.AtLeast(modes, WatercoolerPermissions.SpeakWatercooler, GrantMode.Direct)
            || PermissionModeMap.AtLeast(modes, WatercoolerPermissions.WatercoolerAdministrator, GrantMode.Direct);
    }

    private async Task<bool> CanAccessPendingForumAsync(
        Guid? userId,
        Domain.Entities.Forum forum,
        CancellationToken ct)
    {
        if (ForumApprovalStatuses.IsApproved(forum.ApprovalStatus))
            return true;

        if (!userId.HasValue) return false;
        if (forum.CreatedByUserId == userId.Value) return true;

        if (await IsSuperUserAsync(userId, ct)) return true;

        var serverModes = await GetServerPermissionModesAsync(userId, ct);
        return PermissionModeMap.AtLeast(serverModes, ServerPermissions.CreateForum, GrantMode.Direct);
    }

    private async Task<Dictionary<string, GrantMode>> GetForumOnlyPermissionModesAsync(
        Guid? userId,
        Guid forumId,
        CancellationToken ct)
    {
        // Evaluate the target forum only — do not OR-merge ancestor role maps.
        var everyone = await _db.ForumRoles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ForumId == forumId && r.IsEveryone, ct);

        var modes = CloneModes(everyone?.Modes);

        if (!userId.HasValue)
            return ExpandForumModes(modes);

        var roleIds = await _db.ForumRoleAssignments
            .Where(a => a.ForumId == forumId && a.UserId == userId.Value)
            .Select(a => a.RoleId)
            .ToListAsync(ct);

        if (roleIds.Count == 0)
            return ExpandForumModes(modes);

        var roles = await _db.ForumRoles
            .AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .ToListAsync(ct);

        return ExpandForumModes(MergeRoleModes(everyone?.Modes, roles.Select(r => r.Modes)));
    }

    private static Dictionary<string, GrantMode> ExpandWatercoolerModes(Dictionary<string, GrantMode> modes)
    {
        if (PermissionModeMap.Get(modes, WatercoolerPermissions.WatercoolerAdministrator) == GrantMode.Direct)
            PermissionModeMap.ApplyWatercoolerAdministratorImplied(modes);

        if (PermissionModeMap.AtLeast(modes, WatercoolerPermissions.ViewWatercooler, GrantMode.Direct) &&
            PermissionModeMap.Get(modes, WatercoolerPermissions.SeeWatercooler) == GrantMode.None)
        {
            modes[nameof(WatercoolerPermissions.SeeWatercooler)] = GrantMode.Direct;
        }

        return modes;
    }

    private static Dictionary<string, GrantMode> MergeRoleModes(
        IReadOnlyDictionary<string, GrantMode>? everyone,
        IEnumerable<IReadOnlyDictionary<string, GrantMode>> assigned)
    {
        var maps = new List<IReadOnlyDictionary<string, GrantMode>?>();
        if (everyone is not null)
            maps.Add(everyone);
        maps.AddRange(assigned);
        return PermissionModeMap.MergeMax(maps.ToArray());
    }

    private static Dictionary<string, GrantMode> ExpandServerModes(Dictionary<string, GrantMode> modes)
    {
        if (PermissionModeMap.Get(modes, ServerPermissions.ServerAdministrator) == GrantMode.Direct)
            PermissionModeMap.ApplyServerAdministratorImplied(modes);
        return modes;
    }

    private static Dictionary<string, GrantMode> ExpandForumModes(Dictionary<string, GrantMode> modes)
    {
        if (PermissionModeMap.Get(modes, ForumPermissions.ForumAdministrator) == GrantMode.Direct)
            PermissionModeMap.ApplyForumAdministratorImplied(modes);

        if (PermissionModeMap.AtLeast(modes, ForumPermissions.ViewForum, GrantMode.Direct) &&
            PermissionModeMap.Get(modes, ForumPermissions.SeeForum) == GrantMode.None)
        {
            modes[nameof(ForumPermissions.SeeForum)] = GrantMode.Direct;
        }

        if (PermissionModeMap.AtLeast(modes, ForumPermissions.ViewForum, GrantMode.Direct) &&
            PermissionModeMap.Get(modes, ForumPermissions.SeeContentBlocks) == GrantMode.None)
        {
            modes[nameof(ForumPermissions.SeeContentBlocks)] = GrantMode.Direct;
        }

        return modes;
    }

    private static Dictionary<string, GrantMode> MergeServerIntoForumModes(
        IReadOnlyDictionary<string, GrantMode> server,
        Dictionary<string, GrantMode> forum)
    {
        if (!PermissionModeMap.AtLeast(server, ServerPermissions.ViewServer, GrantMode.Direct))
        {
            forum.Remove(nameof(ForumPermissions.ViewForum));
            forum.Remove(nameof(ForumPermissions.SeeForum));
            forum.Remove(nameof(ForumPermissions.SeeContentBlocks));
        }

        return forum;
    }

    private async Task<Dictionary<string, GrantMode>> ClipViewNsfwAsync(
        Guid? userId,
        Guid forumId,
        Dictionary<string, GrantMode> modes,
        CancellationToken ct)
    {
        if (_nsfwAge is null) return modes;
        if (!PermissionModeMap.AtLeast(modes, ForumPermissions.ViewNsfwMedia, GrantMode.Direct))
            return modes;
        if (await _nsfwAge.ViewerMayViewNsfwAsync(userId, forumId, ct))
            return modes;
        modes.Remove(nameof(ForumPermissions.ViewNsfwMedia));
        return modes;
    }

    private async Task<ExtensionInstall?> TryGetBotInstallAsync(Guid? userId, CancellationToken ct)
    {
        if (!userId.HasValue) return null;
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
        if (user is not { IsBot: true, ExtensionInstallId: { } installId })
            return null;
        return await _db.ExtensionInstalls.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == installId && i.RevokedAt == null, ct);
    }

    private static Dictionary<string, GrantMode> CloneModes(IReadOnlyDictionary<string, GrantMode>? source) =>
        source is null
            ? new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, GrantMode>(source, StringComparer.OrdinalIgnoreCase);
}
