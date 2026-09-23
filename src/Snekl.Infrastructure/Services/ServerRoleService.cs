using Microsoft.EntityFrameworkCore;
using Snekl.Domain;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IServerRoleService
{
    Task<IReadOnlyList<ServerRoleDto>> ListRolesAsync(CancellationToken ct = default);
    Task<ServerRoleDto> CreateRoleAsync(CreateServerRoleRequest request, Guid? actorUserId = null, CancellationToken ct = default);
    Task<ServerRoleDto> UpdateRoleAsync(Guid roleId, UpdateServerRoleRequest request, Guid? actorUserId = null, CancellationToken ct = default);
    Task DeleteRoleAsync(Guid roleId, Guid? actorUserId = null, CancellationToken ct = default);
    Task<IReadOnlyList<ServerMemberDto>> ListMembersAsync(CancellationToken ct = default);
    Task AssignRolesAsync(Guid userId, IReadOnlyList<Guid> roleIds, Guid? actorUserId = null, CancellationToken ct = default);
    Task AssignRolesByUsernameAsync(string username, IReadOnlyList<Guid> roleIds, Guid? actorUserId = null, CancellationToken ct = default);
    Task<IReadOnlyList<ServerRoleDto>> ListEffectiveRolesForUserAsync(Guid userId, CancellationToken ct = default);
}

public class ServerRoleService : IServerRoleService
{
    private readonly SneklDbContext _db;
    private readonly IChangeLogService? _changeLog;
    private readonly IPermissionService _permissions;

    public ServerRoleService(SneklDbContext db, IPermissionService permissions, IChangeLogService? changeLog = null)
    {
        _db = db;
        _permissions = permissions;
        _changeLog = changeLog;
    }

    public async Task<IReadOnlyList<ServerRoleDto>> ListRolesAsync(CancellationToken ct = default)
    {
        var roles = await _db.ServerRoles.OrderBy(r => r.Position).ToListAsync(ct);
        return roles.Select(ToDto).ToList();
    }

    public async Task<ServerRoleDto> CreateRoleAsync(CreateServerRoleRequest request, Guid? actorUserId = null, CancellationToken ct = default)
    {
        if (request.Name.Trim().Equals("@everyone", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot create a role named @everyone.");

        var role = new ServerRole
        {
            Name = request.Name.Trim(),
            Color = request.Color,
            Position = request.Position,
            Modes = PermissionParsing.ParseServerModes(request.Permissions, await CustomServerNamesAsync(ct))
        };
        _db.ServerRoles.Add(role);
        await _db.SaveChangesAsync(ct);
        await RecordRoleAsync(role, ChangeLogActions.Created, actorUserId, $"Created server role {role.Name}", null, RoleSnapshot(role), ct);
        return ToDto(role);
    }

    public async Task<ServerRoleDto> UpdateRoleAsync(Guid roleId, UpdateServerRoleRequest request, Guid? actorUserId = null, CancellationToken ct = default)
    {
        var role = await _db.ServerRoles.FindAsync([roleId], ct)
            ?? throw new InvalidOperationException("Role not found.");

        var before = RoleSnapshot(role);
        if (role.IsEveryone)
        {
            role.Modes = PermissionParsing.ParseServerModes(request.Permissions, await CustomServerNamesAsync(ct));
        }
        else
        {
            role.Name = request.Name.Trim();
            role.Color = request.Color;
            role.Position = request.Position;
            role.Modes = PermissionParsing.ParseServerModes(request.Permissions, await CustomServerNamesAsync(ct));
        }

        await _db.SaveChangesAsync(ct);
        await RecordRoleAsync(role, ChangeLogActions.Updated, actorUserId, $"Updated server role {role.Name}", before, RoleSnapshot(role), ct);
        return ToDto(role);
    }

    public async Task DeleteRoleAsync(Guid roleId, Guid? actorUserId = null, CancellationToken ct = default)
    {
        var role = await _db.ServerRoles.FindAsync([roleId], ct)
            ?? throw new InvalidOperationException("Role not found.");
        if (role.IsEveryone)
            throw new InvalidOperationException("Cannot delete @everyone role.");

        var before = RoleSnapshot(role);
        var name = role.Name;
        _db.ServerRoles.Remove(role);
        await _db.SaveChangesAsync(ct);
        await RecordRoleAsync(role, ChangeLogActions.Deleted, actorUserId, $"Deleted server role {name}", before, null, ct);
    }

    public async Task<IReadOnlyList<ServerMemberDto>> ListMembersAsync(CancellationToken ct = default)
    {
        var assignments = await _db.ServerRoleAssignments
            .Include(a => a.User)
            .Include(a => a.Role)
            .ToListAsync(ct);

        var grouped = assignments.GroupBy(a => a.UserId);
        var result = new List<ServerMemberDto>();

        foreach (var group in grouped)
        {
            var user = group.First().User!;
            result.Add(new ServerMemberDto(
                user.Id,
                user.Username,
                user.DisplayName,
                group.Select(a => ToDto(a.Role!)).ToList()));
        }

        return result.OrderBy(m => m.Username).ToList();
    }

    public async Task AssignRolesAsync(Guid userId, IReadOnlyList<Guid> roleIds, Guid? actorUserId = null, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct)
            ?? throw new InvalidOperationException("User not found.");

        if (await _permissions.IsServerOwnerAsync(userId, ct) &&
            (!actorUserId.HasValue || actorUserId.Value != userId))
        {
            throw new InvalidOperationException("Only the server owner can change their own server roles.");
        }

        var validRoleIds = await _db.ServerRoles
            .Where(r => roleIds.Contains(r.Id) && !r.IsEveryone)
            .Select(r => r.Id)
            .ToListAsync(ct);

        var existing = await _db.ServerRoleAssignments
            .Where(a => a.UserId == userId)
            .ToListAsync(ct);
        var beforeIds = existing.Select(a => a.RoleId).OrderBy(id => id).ToList();
        _db.ServerRoleAssignments.RemoveRange(existing);

        foreach (var roleId in validRoleIds)
        {
            _db.ServerRoleAssignments.Add(new ServerRoleAssignment
            {
                UserId = userId,
                RoleId = roleId
            });
        }

        await _db.SaveChangesAsync(ct);
        var afterIds = validRoleIds.OrderBy(id => id).ToList();
        if (_changeLog is not null && !beforeIds.SequenceEqual(afterIds))
        {
            await _changeLog.RecordAsync(
                ChangeLogScopes.Server,
                null,
                null,
                ChangeLogEntityTypes.ServerMember,
                userId,
                ChangeLogActions.Assigned,
                actorUserId,
                $"Updated server roles for @{user.Username}",
                new { roleIds = beforeIds },
                new { roleIds = afterIds },
                ct);
        }
    }

    public async Task AssignRolesByUsernameAsync(string username, IReadOnlyList<Guid> roleIds, Guid? actorUserId = null, CancellationToken ct = default)
    {
        var normalized = username.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == normalized, ct)
            ?? throw new InvalidOperationException("User not found.");
        await AssignRolesAsync(user.Id, roleIds, actorUserId, ct);
    }

    public async Task<IReadOnlyList<ServerRoleDto>> ListEffectiveRolesForUserAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var everyone = await _db.ServerRoles.Where(r => r.IsEveryone).OrderBy(r => r.Position).ToListAsync(ct);
        var assignedIds = await _db.ServerRoleAssignments
            .Where(a => a.UserId == userId)
            .Select(a => a.RoleId)
            .ToListAsync(ct);
        var assigned = await _db.ServerRoles
            .Where(r => assignedIds.Contains(r.Id) && !r.IsEveryone)
            .OrderBy(r => r.Position)
            .ToListAsync(ct);

        return everyone.Concat(assigned).Select(ToDto).ToList();
    }

    private static ServerRoleDto ToDto(ServerRole role) => new(
        role.Id,
        role.Name,
        role.Color,
        role.Position,
        PermissionNames.ToNames(role.Modes, server: true),
        role.IsEveryone);

    private static object RoleSnapshot(ServerRole role) => new
    {
        name = role.Name,
        color = role.Color,
        position = role.Position,
        permissions = PermissionNames.ToNames(role.Modes, server: true)
    };

    private async Task RecordRoleAsync(
        ServerRole role,
        string action,
        Guid? actorUserId,
        string summary,
        object? before,
        object? after,
        CancellationToken ct)
    {
        if (_changeLog is null) return;
        await _changeLog.RecordAsync(
            ChangeLogScopes.Server,
            null,
            null,
            ChangeLogEntityTypes.ServerRole,
            role.Id,
            action,
            actorUserId,
            summary,
            before,
            after,
            ct);
    }

    private async Task<HashSet<string>> CustomServerNamesAsync(CancellationToken ct)
    {
        var names = await _db.ContentBlockPermissions.AsNoTracking()
            .Where(p => p.Scope == ContentBlockScopes.Server)
            .Select(p => p.Name)
            .ToListAsync(ct);
        return names.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
