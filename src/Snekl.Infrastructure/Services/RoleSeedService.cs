using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IRoleSeedService
{
    Task EnsureServerRolesAsync(CancellationToken ct = default);
    Task SeedForumRolesAsync(Guid forumId, Guid creatorUserId, CancellationToken ct = default);
    Task InheritForumRolesFromParentAsync(Guid subforumId, Guid parentForumId, CancellationToken ct = default);
    Task EnsureForumOwnerAsync(Guid forumId, Guid ownerUserId, CancellationToken ct = default);
    Task<ForumRole> EnsureMemberRoleAsync(Guid forumId, CancellationToken ct = default);
    Task SeedWatercoolerRolesAsync(Guid watercoolerId, Guid creatorUserId, Guid? parentForumId, CancellationToken ct = default);
}

public class RoleSeedService : IRoleSeedService
{
    private readonly SneklDbContext _db;

    public RoleSeedService(SneklDbContext db) => _db = db;

    public async Task EnsureServerRolesAsync(CancellationToken ct = default)
    {
        if (!await _db.ServerRoles.AnyAsync(r => r.IsEveryone, ct))
        {
            _db.ServerRoles.Add(new ServerRole
            {
                Name = "@everyone",
                Position = 0,
                IsEveryone = true,
                Modes = PermissionModeMap.FromServerFlags(ServerPermissions.ViewServer)
            });
        }

        if (!await _db.ServerRoles.AnyAsync(r => r.Name == "Member", ct))
        {
            _db.ServerRoles.Add(new ServerRole
            {
                Name = "Member",
                Position = 1,
                Color = "#4f8cff",
                Modes = PermissionModeMap.FromServerFlags(
                    ServerPermissions.ViewServer | ServerPermissions.CreateForum |
                    ServerPermissions.CreateWatercooler)
            });
        }

        if (!await _db.ServerRoles.AnyAsync(r => r.Name == "Moderator", ct))
        {
            _db.ServerRoles.Add(new ServerRole
            {
                Name = "Moderator",
                Position = 2,
                Color = "#43b581",
                Modes = PermissionModeMap.FromServerFlags(
                    ServerPermissions.ViewServer | ServerPermissions.CreateForum |
                    ServerPermissions.CreateWatercooler |
                    ServerPermissions.ManageServerMembers | ServerPermissions.ManageContentBlocks |
                    ServerPermissions.ViewHistory)
            });
        }

        var nestRequester = await _db.ServerRoles.FirstOrDefaultAsync(r => r.Name == "Nest requester" || r.Name == "Forum requester", ct);
        if (nestRequester is null)
        {
            _db.ServerRoles.Add(new ServerRole
            {
                Name = "Nest requester",
                Position = 3,
                Color = "#faa61a",
                Modes = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase)
                {
                    [nameof(ServerPermissions.ViewServer)] = GrantMode.Direct,
                    [nameof(ServerPermissions.CreateForum)] = GrantMode.Request,
                    [nameof(ServerPermissions.CreateWatercooler)] = GrantMode.Request
                }
            });
        }
        else if (nestRequester.Name == "Forum requester")
            nestRequester.Name = "Nest requester";

        await _db.SaveChangesAsync(ct);
    }

    public async Task SeedForumRolesAsync(Guid forumId, Guid creatorUserId, CancellationToken ct = default)
    {
        var everyone = new ForumRole
        {
            ForumId = forumId,
            Name = "@everyone",
            Position = 0,
            IsEveryone = true,
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum |
                ForumPermissions.SeeContentBlocks |
                ForumPermissions.CreateThread | ForumPermissions.Reply)
        };
        _db.ForumRoles.Add(everyone);

        var member = new ForumRole
        {
            ForumId = forumId,
            Name = "Member",
            Position = 1,
            Color = "#4f8cff",
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum |
                ForumPermissions.SeeContentBlocks |
                ForumPermissions.CreateThread | ForumPermissions.Reply)
        };
        _db.ForumRoles.Add(member);

        var owner = new ForumRole
        {
            ForumId = forumId,
            Name = "Owner",
            Position = 100,
            Color = "#e74c3c",
            Modes = PermissionModeMap.FromForumFlags(ForumPermissions.ForumAdministrator),
            IsOwner = true
        };
        _db.ForumRoles.Add(owner);
        await _db.SaveChangesAsync(ct);

        _db.ForumRoleAssignments.Add(new ForumRoleAssignment
        {
            ForumId = forumId,
            UserId = creatorUserId,
            RoleId = owner.Id
        });
        await _db.SaveChangesAsync(ct);

        if (!await _db.ForumRoles.AnyAsync(r => r.ForumId == forumId && r.Name == "Moderator", ct))
        {
            _db.ForumRoles.Add(new ForumRole
            {
                ForumId = forumId,
                Name = "Moderator",
                Position = 50,
                Color = "#43b581",
                Modes = PermissionModeMap.FromForumFlags(
                    ForumPermissions.SeeForum | ForumPermissions.ViewForum |
                    ForumPermissions.SeeContentBlocks |
                    ForumPermissions.CreateThread | ForumPermissions.Reply |
                    ForumPermissions.EditOwnPost | ForumPermissions.DeleteOwnPost |
                    ForumPermissions.ManagePosts | ForumPermissions.ManageMembers |
                    ForumPermissions.ManageContentBlocks |
                    ForumPermissions.RequestDirectMessage |
                    ForumPermissions.ViewHistory |
                    ForumPermissions.UseOtherForumEmotes)
            });
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task InheritForumRolesFromParentAsync(Guid subforumId, Guid parentForumId, CancellationToken ct = default)
    {
        var parentRoles = await _db.ForumRoles
            .Where(r => r.ForumId == parentForumId)
            .OrderBy(r => r.Position)
            .ToListAsync(ct);

        foreach (var parentRole in parentRoles)
        {
            var inherited = new ForumRole
            {
                ForumId = subforumId,
                Name = parentRole.Name,
                Color = parentRole.Color,
                Position = parentRole.Position,
                Modes = new Dictionary<string, GrantMode>(parentRole.Modes, StringComparer.OrdinalIgnoreCase),
                IsEveryone = parentRole.IsEveryone,
                IsOwner = parentRole.IsOwner || parentRole.Name.Equals("Owner", StringComparison.OrdinalIgnoreCase),
                InheritFromRoleId = parentRole.Id
            };
            _db.ForumRoles.Add(inherited);
            await _db.SaveChangesAsync(ct);
        }

        var parentFlairs = await _db.ForumFlairTemplates
            .Where(t => t.ForumId == parentForumId)
            .ToListAsync(ct);

        foreach (var flair in parentFlairs)
        {
            _db.ForumFlairTemplates.Add(new ForumFlairTemplate
            {
                ForumId = subforumId,
                Emoji = flair.Emoji,
                IconUrl = flair.IconUrl,
                Label = flair.Label,
                NameColor = flair.NameColor,
                NameStyle = flair.NameStyle,
                Position = flair.Position,
                InheritFromTemplateId = flair.Id
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task EnsureForumOwnerAsync(Guid forumId, Guid ownerUserId, CancellationToken ct = default)
    {
        var ownerRole = await _db.ForumRoles.FirstOrDefaultAsync(r => r.ForumId == forumId && r.IsOwner, ct);
        if (ownerRole is null)
        {
            ownerRole = await _db.ForumRoles.FirstOrDefaultAsync(
                r => r.ForumId == forumId && r.Name.Equals("Owner", StringComparison.OrdinalIgnoreCase),
                ct);
            if (ownerRole is not null)
            {
                ownerRole.IsOwner = true;
            }
            else
            {
                ownerRole = new ForumRole
                {
                    ForumId = forumId,
                    Name = "Owner",
                    Position = 100,
                    Color = "#e74c3c",
                    Modes = PermissionModeMap.FromForumFlags(ForumPermissions.ForumAdministrator),
                    IsOwner = true
                };
                _db.ForumRoles.Add(ownerRole);
                await _db.SaveChangesAsync(ct);
            }
        }

        var existing = await _db.ForumRoleAssignments
            .Where(a => a.ForumId == forumId && a.RoleId == ownerRole.Id)
            .ToListAsync(ct);
        foreach (var assignment in existing.Where(a => a.UserId != ownerUserId))
            _db.ForumRoleAssignments.Remove(assignment);

        if (!existing.Any(a => a.UserId == ownerUserId))
        {
            _db.ForumRoleAssignments.Add(new ForumRoleAssignment
            {
                ForumId = forumId,
                UserId = ownerUserId,
                RoleId = ownerRole.Id
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<ForumRole> EnsureMemberRoleAsync(Guid forumId, CancellationToken ct = default)
    {
        var existing = await _db.ForumRoles.FirstOrDefaultAsync(
            r => r.ForumId == forumId && r.Name == "Member",
            ct);
        if (existing is not null)
            return existing;

        var role = new ForumRole
        {
            ForumId = forumId,
            Name = "Member",
            Position = 1,
            Color = "#4f8cff",
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum |
                ForumPermissions.SeeContentBlocks |
                ForumPermissions.CreateThread | ForumPermissions.Reply)
        };
        _db.ForumRoles.Add(role);
        await _db.SaveChangesAsync(ct);
        return role;
    }

    public async Task SeedWatercoolerRolesAsync(
        Guid watercoolerId,
        Guid creatorUserId,
        Guid? parentForumId,
        CancellationToken ct = default)
    {
        Dictionary<string, GrantMode> everyoneModes;
        Dictionary<string, GrantMode> memberModes;
        Dictionary<string, GrantMode> moderatorModes;

        if (parentForumId.HasValue)
        {
            var parentEveryone = await _db.ForumRoles.AsNoTracking()
                .FirstOrDefaultAsync(r => r.ForumId == parentForumId.Value && r.IsEveryone, ct);
            everyoneModes = MapForumModesToWatercooler(parentEveryone?.Modes);
            var parentMember = await _db.ForumRoles.AsNoTracking()
                .FirstOrDefaultAsync(r => r.ForumId == parentForumId.Value && r.Name == "Member", ct);
            memberModes = MapForumModesToWatercooler(parentMember?.Modes ?? parentEveryone?.Modes);
            var parentMod = await _db.ForumRoles.AsNoTracking()
                .FirstOrDefaultAsync(r => r.ForumId == parentForumId.Value && r.Name == "Moderator", ct);
            moderatorModes = MapForumModesToWatercooler(parentMod?.Modes);
        }
        else
        {
            everyoneModes = PermissionModeMap.FromWatercoolerFlags(
                WatercoolerPermissions.SeeWatercooler | WatercoolerPermissions.ViewWatercooler |
                WatercoolerPermissions.SpeakWatercooler);
            memberModes = PermissionModeMap.FromWatercoolerFlags(
                WatercoolerPermissions.SeeWatercooler | WatercoolerPermissions.ViewWatercooler |
                WatercoolerPermissions.SpeakWatercooler);
            moderatorModes = PermissionModeMap.FromWatercoolerFlags(
                WatercoolerPermissions.SeeWatercooler | WatercoolerPermissions.ViewWatercooler |
                WatercoolerPermissions.SpeakWatercooler | WatercoolerPermissions.ManageWatercooler |
                WatercoolerPermissions.ViewHistory);
        }

        if (everyoneModes.Count == 0)
        {
            everyoneModes = PermissionModeMap.FromWatercoolerFlags(
                WatercoolerPermissions.SeeWatercooler | WatercoolerPermissions.ViewWatercooler);
        }

        var everyone = new WatercoolerRole
        {
            WatercoolerId = watercoolerId,
            Name = "@everyone",
            Position = 0,
            IsEveryone = true,
            Modes = everyoneModes
        };
        _db.WatercoolerRoles.Add(everyone);

        _db.WatercoolerRoles.Add(new WatercoolerRole
        {
            WatercoolerId = watercoolerId,
            Name = "Member",
            Position = 1,
            Color = "#4f8cff",
            Modes = memberModes.Count > 0 ? memberModes : everyoneModes
        });

        var owner = new WatercoolerRole
        {
            WatercoolerId = watercoolerId,
            Name = "Owner",
            Position = 100,
            Color = "#e74c3c",
            Modes = PermissionModeMap.FromWatercoolerFlags(WatercoolerPermissions.WatercoolerAdministrator)
        };
        _db.WatercoolerRoles.Add(owner);
        await _db.SaveChangesAsync();

        _db.WatercoolerRoleAssignments.Add(new WatercoolerRoleAssignment
        {
            WatercoolerId = watercoolerId,
            UserId = creatorUserId,
            RoleId = owner.Id
        });

        _db.WatercoolerRoles.Add(new WatercoolerRole
        {
            WatercoolerId = watercoolerId,
            Name = "Moderator",
            Position = 50,
            Color = "#43b581",
            Modes = moderatorModes.Count > 0
                ? moderatorModes
                : PermissionModeMap.FromWatercoolerFlags(
                    WatercoolerPermissions.SeeWatercooler | WatercoolerPermissions.ViewWatercooler |
                    WatercoolerPermissions.SpeakWatercooler | WatercoolerPermissions.ManageWatercooler |
                    WatercoolerPermissions.ViewHistory)
        });
        await _db.SaveChangesAsync(ct);
    }

    private static Dictionary<string, GrantMode> MapForumModesToWatercooler(
        IReadOnlyDictionary<string, GrantMode>? forumModes)
    {
        var result = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase);
        if (forumModes is null) return result;

        void Map(ForumPermissions from, WatercoolerPermissions to)
        {
            var mode = PermissionModeMap.Get(forumModes, from);
            if (mode != GrantMode.None)
                result[to.ToString()] = mode;
        }

        Map(ForumPermissions.SeeForum, WatercoolerPermissions.SeeWatercooler);
        Map(ForumPermissions.ViewForum, WatercoolerPermissions.ViewWatercooler);
        Map(ForumPermissions.ViewForum, WatercoolerPermissions.SpeakWatercooler);
        Map(ForumPermissions.ViewHistory, WatercoolerPermissions.ViewHistory);

        var manage = GrantMode.None;
        foreach (var flag in new[] { ForumPermissions.ManageForum, ForumPermissions.ManagePosts, ForumPermissions.ForumAdministrator })
        {
            var mode = PermissionModeMap.Get(forumModes, flag);
            if (mode > manage) manage = mode;
        }

        if (manage != GrantMode.None)
            result[nameof(WatercoolerPermissions.ManageWatercooler)] = manage;

        if (PermissionModeMap.Get(forumModes, ForumPermissions.ForumAdministrator) == GrantMode.Direct)
            result[nameof(WatercoolerPermissions.WatercoolerAdministrator)] = GrantMode.Direct;

        return PermissionModeMap.FromWatercoolerFlags(PermissionModeMap.ToWatercoolerFlags(result));
    }
}
