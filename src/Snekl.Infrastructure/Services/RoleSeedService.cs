using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IRoleSeedService
{
    Task EnsureServerRolesAsync(CancellationToken ct = default);
    Task SeedForumRolesAsync(Guid forumId, Guid creatorUserId, CancellationToken ct = default);
    Task InheritForumRolesFromParentAsync(Guid subforumId, Guid parentForumId, CancellationToken ct = default);
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
                Permissions = ServerPermissions.ViewServer
            });
        }

        if (!await _db.ServerRoles.AnyAsync(r => r.Name == "Member", ct))
        {
            _db.ServerRoles.Add(new ServerRole
            {
                Name = "Member",
                Position = 1,
                Color = "#4f8cff",
                Permissions = ServerPermissions.ViewServer | ServerPermissions.CreateForum
            });
        }

        if (!await _db.ServerRoles.AnyAsync(r => r.Name == "Moderator", ct))
        {
            _db.ServerRoles.Add(new ServerRole
            {
                Name = "Moderator",
                Position = 2,
                Color = "#43b581",
                Permissions = ServerPermissions.ViewServer | ServerPermissions.CreateForum |
                              ServerPermissions.ManageServerMembers
            });
        }

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
            Permissions = ForumPermissions.ViewForum | ForumPermissions.CreateThread | ForumPermissions.Reply
        };
        _db.ForumRoles.Add(everyone);

        var owner = new ForumRole
        {
            ForumId = forumId,
            Name = "Owner",
            Position = 100,
            Color = "#e74c3c",
            Permissions = ForumPermissions.ForumAdministrator
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
                Permissions = ForumPermissions.ViewForum | ForumPermissions.CreateThread | ForumPermissions.Reply |
                              ForumPermissions.EditOwnPost | ForumPermissions.DeleteOwnPost |
                              ForumPermissions.ManagePosts | ForumPermissions.ManageMembers
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

        var roleMap = new Dictionary<Guid, Guid>();

        foreach (var parentRole in parentRoles)
        {
            var inherited = new ForumRole
            {
                ForumId = subforumId,
                Name = parentRole.Name,
                Color = parentRole.Color,
                Position = parentRole.Position,
                Permissions = parentRole.Permissions,
                IsEveryone = parentRole.IsEveryone,
                InheritFromRoleId = parentRole.Id
            };
            _db.ForumRoles.Add(inherited);
            await _db.SaveChangesAsync(ct);
            roleMap[parentRole.Id] = inherited.Id;
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
}
