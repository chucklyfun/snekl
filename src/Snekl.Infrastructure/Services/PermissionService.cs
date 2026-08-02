using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IPermissionService
{
    Task<bool> IsSuperUserAsync(Guid? userId, CancellationToken ct = default);
    Task<ServerPermissions> GetServerPermissionsAsync(Guid? userId, CancellationToken ct = default);
    Task<ForumPermissions> GetForumPermissionsAsync(Guid? userId, Guid forumId, CancellationToken ct = default);
    Task RequireServerPermissionAsync(Guid userId, ServerPermissions flag, CancellationToken ct = default);
    Task RequireForumPermissionAsync(Guid userId, Guid forumId, ForumPermissions flag, CancellationToken ct = default);
    Task<bool> CanViewForumAsync(Guid? userId, Guid forumId, CancellationToken ct = default);
}

public class PermissionService : IPermissionService
{
    private readonly SneklDbContext _db;

    public PermissionService(SneklDbContext db) => _db = db;

    public Task<bool> IsSuperUserAsync(Guid? userId, CancellationToken ct = default) =>
        userId.HasValue
            ? _db.SuperUserGrants.AnyAsync(g => g.UserId == userId.Value, ct)
            : Task.FromResult(false);

    public async Task<ServerPermissions> GetServerPermissionsAsync(Guid? userId, CancellationToken ct = default)
    {
        if (userId.HasValue && await IsSuperUserAsync(userId, ct))
            return ServerPermissions.ViewServer | ServerPermissions.CreateForum |
                   ServerPermissions.ManageServerMembers | ServerPermissions.ManageServerRoles |
                   ServerPermissions.ManageServerSettings | ServerPermissions.ServerAdministrator;

        var everyone = await _db.ServerRoles.FirstOrDefaultAsync(r => r.IsEveryone, ct);
        var perms = everyone?.Permissions ?? ServerPermissions.None;

        if (!userId.HasValue) return perms;

        var roleIds = await _db.ServerRoleAssignments
            .Where(a => a.UserId == userId.Value)
            .Select(a => a.RoleId)
            .ToListAsync(ct);

        if (roleIds.Count == 0) return perms;

        var roles = await _db.ServerRoles
            .Where(r => roleIds.Contains(r.Id))
            .OrderByDescending(r => r.Position)
            .ToListAsync(ct);

        return MergeServerRoles(everyone, roles);
    }

    public async Task<ForumPermissions> GetForumPermissionsAsync(Guid? userId, Guid forumId, CancellationToken ct = default)
    {
        var serverPerms = await GetServerPermissionsAsync(userId, ct);
        var forumPerms = await GetForumOnlyPermissionsAsync(userId, forumId, ct);
        return MergeServerIntoForum(serverPerms, forumPerms);
    }

    public async Task RequireServerPermissionAsync(Guid userId, ServerPermissions flag, CancellationToken ct = default)
    {
        var perms = await GetServerPermissionsAsync(userId, ct);
        if (!perms.HasFlag(flag))
            throw new PermissionDeniedException(flag.ToString(), "server");
    }

    public async Task RequireForumPermissionAsync(Guid userId, Guid forumId, ForumPermissions flag, CancellationToken ct = default)
    {
        var perms = await GetForumPermissionsAsync(userId, forumId, ct);
        if (!perms.HasFlag(flag))
            throw new PermissionDeniedException(flag.ToString(), "forum");
    }

    public async Task<bool> CanViewForumAsync(Guid? userId, Guid forumId, CancellationToken ct = default)
    {
        var perms = await GetForumPermissionsAsync(userId, forumId, ct);
        return perms.HasFlag(ForumPermissions.ViewForum);
    }

    private async Task<ForumPermissions> GetForumOnlyPermissionsAsync(Guid? userId, Guid forumId, CancellationToken ct)
    {
        var chain = await GetForumChainAsync(forumId, ct);
        ForumPermissions merged = ForumPermissions.None;

        foreach (var forum in chain)
        {
            var everyone = await _db.ForumRoles
                .FirstOrDefaultAsync(r => r.ForumId == forum.Id && r.IsEveryone, ct);

            var forumPerms = everyone?.Permissions ?? ForumPermissions.None;

            if (userId.HasValue)
            {
                var roleIds = await _db.ForumRoleAssignments
                    .Where(a => a.ForumId == forum.Id && a.UserId == userId.Value)
                    .Select(a => a.RoleId)
                    .ToListAsync(ct);

                if (roleIds.Count > 0)
                {
                    var roles = await _db.ForumRoles
                        .Where(r => roleIds.Contains(r.Id))
                        .OrderByDescending(r => r.Position)
                        .ToListAsync(ct);
                    forumPerms = MergeForumRoles(everyone, roles);
                }
            }

            merged |= forumPerms;
        }

        return merged;
    }

    private async Task<List<Domain.Entities.Forum>> GetForumChainAsync(Guid forumId, CancellationToken ct)
    {
        var chain = new List<Domain.Entities.Forum>();
        var currentId = (Guid?)forumId;

        while (currentId.HasValue)
        {
            var forum = await _db.Forums.AsNoTracking().FirstOrDefaultAsync(f => f.Id == currentId.Value, ct);
            if (forum is null) break;
            chain.Insert(0, forum);
            currentId = forum.ParentForumId;
        }

        return chain;
    }

    private static ServerPermissions MergeServerRoles(Domain.Entities.ServerRole? everyone, IReadOnlyList<Domain.Entities.ServerRole> assigned)
    {
        var allRoles = assigned.ToList();
        if (everyone is not null && !allRoles.Any(r => r.IsEveryone))
            allRoles.Add(everyone);

        var ordered = allRoles.OrderByDescending(r => r.Position).ToList();
        var result = ServerPermissions.None;
        foreach (var role in ordered)
            result |= role.Permissions;

        if (result.HasFlag(ServerPermissions.ServerAdministrator))
            result |= ServerPermissions.ViewServer | ServerPermissions.CreateForum |
                      ServerPermissions.ManageServerMembers | ServerPermissions.ManageServerRoles |
                      ServerPermissions.ManageServerSettings;

        return result;
    }

    private static ForumPermissions MergeForumRoles(Domain.Entities.ForumRole? everyone, IReadOnlyList<Domain.Entities.ForumRole> assigned)
    {
        var allRoles = assigned.ToList();
        if (everyone is not null && !allRoles.Any(r => r.IsEveryone))
            allRoles.Add(everyone);

        var ordered = allRoles.OrderByDescending(r => r.Position).ToList();
        var result = ForumPermissions.None;
        foreach (var role in ordered)
            result |= role.Permissions;

        if (result.HasFlag(ForumPermissions.ForumAdministrator))
            result |= ForumPermissions.ViewForum | ForumPermissions.CreateThread | ForumPermissions.Reply |
                      ForumPermissions.EditOwnPost | ForumPermissions.DeleteOwnPost | ForumPermissions.ManagePosts |
                      ForumPermissions.ManageMembers | ForumPermissions.ManageRoles | ForumPermissions.ManageForum;

        return result;
    }

    private static ForumPermissions MergeServerIntoForum(ServerPermissions server, ForumPermissions forum)
    {
        var result = forum;
        if (server.HasFlag(ServerPermissions.ServerAdministrator))
        {
            result |= ForumPermissions.ViewForum | ForumPermissions.CreateThread | ForumPermissions.Reply |
                      ForumPermissions.ManagePosts | ForumPermissions.ManageMembers | ForumPermissions.ManageRoles |
                      ForumPermissions.ManageForum | ForumPermissions.ForumAdministrator;
        }
        if (!server.HasFlag(ServerPermissions.ViewServer))
            result &= ~ForumPermissions.ViewForum;
        return result;
    }
}
