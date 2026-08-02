using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IServerRoleService
{
    Task<IReadOnlyList<ServerRoleDto>> ListRolesAsync(CancellationToken ct = default);
    Task<ServerRoleDto> CreateRoleAsync(CreateServerRoleRequest request, CancellationToken ct = default);
    Task<ServerRoleDto> UpdateRoleAsync(Guid roleId, UpdateServerRoleRequest request, CancellationToken ct = default);
    Task DeleteRoleAsync(Guid roleId, CancellationToken ct = default);
    Task<IReadOnlyList<ServerMemberDto>> ListMembersAsync(CancellationToken ct = default);
    Task AssignRolesAsync(Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default);
    Task AssignRolesByUsernameAsync(string username, IReadOnlyList<Guid> roleIds, CancellationToken ct = default);
}

public class ServerRoleService : IServerRoleService
{
    private readonly SneklDbContext _db;

    public ServerRoleService(SneklDbContext db) => _db = db;

    public async Task<IReadOnlyList<ServerRoleDto>> ListRolesAsync(CancellationToken ct = default)
    {
        var roles = await _db.ServerRoles.OrderBy(r => r.Position).ToListAsync(ct);
        return roles.Select(ToDto).ToList();
    }

    public async Task<ServerRoleDto> CreateRoleAsync(CreateServerRoleRequest request, CancellationToken ct = default)
    {
        if (request.Name.Trim().Equals("@everyone", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot create a role named @everyone.");

        var role = new ServerRole
        {
            Name = request.Name.Trim(),
            Color = request.Color,
            Position = request.Position,
            Permissions = PermissionParsing.ParseServerPermissions(request.Permissions)
        };
        _db.ServerRoles.Add(role);
        await _db.SaveChangesAsync(ct);
        return ToDto(role);
    }

    public async Task<ServerRoleDto> UpdateRoleAsync(Guid roleId, UpdateServerRoleRequest request, CancellationToken ct = default)
    {
        var role = await _db.ServerRoles.FindAsync([roleId], ct)
            ?? throw new InvalidOperationException("Role not found.");

        if (role.IsEveryone)
        {
            role.Permissions = PermissionParsing.ParseServerPermissions(request.Permissions);
        }
        else
        {
            role.Name = request.Name.Trim();
            role.Color = request.Color;
            role.Position = request.Position;
            role.Permissions = PermissionParsing.ParseServerPermissions(request.Permissions);
        }

        await _db.SaveChangesAsync(ct);
        return ToDto(role);
    }

    public async Task DeleteRoleAsync(Guid roleId, CancellationToken ct = default)
    {
        var role = await _db.ServerRoles.FindAsync([roleId], ct)
            ?? throw new InvalidOperationException("Role not found.");
        if (role.IsEveryone)
            throw new InvalidOperationException("Cannot delete @everyone role.");

        _db.ServerRoles.Remove(role);
        await _db.SaveChangesAsync(ct);
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

    public async Task AssignRolesAsync(Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct)
            ?? throw new InvalidOperationException("User not found.");

        var validRoleIds = await _db.ServerRoles
            .Where(r => roleIds.Contains(r.Id) && !r.IsEveryone)
            .Select(r => r.Id)
            .ToListAsync(ct);

        var existing = await _db.ServerRoleAssignments
            .Where(a => a.UserId == userId)
            .ToListAsync(ct);
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
    }

    public async Task AssignRolesByUsernameAsync(string username, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
    {
        var normalized = username.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == normalized, ct)
            ?? throw new InvalidOperationException("User not found.");
        await AssignRolesAsync(user.Id, roleIds, ct);
    }

    private static ServerRoleDto ToDto(ServerRole role) => new(
        role.Id,
        role.Name,
        role.Color,
        role.Position,
        PermissionNames.ToNames(role.Permissions),
        role.IsEveryone);
}
