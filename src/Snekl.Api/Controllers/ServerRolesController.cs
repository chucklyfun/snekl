using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/server")]
public class ServerRolesController : ControllerBase
{
    private readonly IServerRoleService _roles;
    private readonly IPermissionService _permissions;

    public ServerRolesController(IServerRoleService roles, IPermissionService permissions)
    {
        _roles = roles;
        _permissions = permissions;
    }

    private async Task RequireManageRolesAsync(Guid userId, CancellationToken ct)
    {
        if (await _permissions.IsSuperUserAsync(userId, ct)) return;
        await _permissions.RequireServerPermissionAsync(userId, ServerPermissions.ManageServerRoles, ct);
    }

    private async Task RequireManageMembersAsync(Guid userId, CancellationToken ct)
    {
        if (await _permissions.IsSuperUserAsync(userId, ct)) return;
        await _permissions.RequireServerPermissionAsync(userId, ServerPermissions.ManageServerMembers, ct);
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("me/permissions")]
    public async Task<ActionResult<ServerPermissionsDto>> MyPermissions(CancellationToken ct)
    {
        var userId = this.RequireUserId();
        var isSuper = await _permissions.IsSuperUserAsync(userId, ct);
        var perms = await _permissions.GetServerPermissionsAsync(userId, ct);
        return Ok(new ServerPermissionsDto(isSuper, PermissionNames.ToNames(perms)));
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("roles")]
    public async Task<ActionResult<IReadOnlyList<ServerRoleDto>>> ListRoles(CancellationToken ct)
    {
        try
        {
            await RequireManageRolesAsync(this.RequireUserId(), ct);
            return Ok(await _roles.ListRolesAsync(ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("roles")]
    public async Task<ActionResult<ServerRoleDto>> CreateRole(CreateServerRoleRequest request, CancellationToken ct)
    {
        try
        {
            await RequireManageRolesAsync(this.RequireUserId(), ct);
            return Ok(await _roles.CreateRoleAsync(request, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("roles/{roleId:guid}")]
    public async Task<ActionResult<ServerRoleDto>> UpdateRole(Guid roleId, UpdateServerRoleRequest request, CancellationToken ct)
    {
        try
        {
            await RequireManageRolesAsync(this.RequireUserId(), ct);
            return Ok(await _roles.UpdateRoleAsync(roleId, request, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpDelete("roles/{roleId:guid}")]
    public async Task<IActionResult> DeleteRole(Guid roleId, CancellationToken ct)
    {
        try
        {
            await RequireManageRolesAsync(this.RequireUserId(), ct);
            await _roles.DeleteRoleAsync(roleId, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("members")]
    public async Task<ActionResult<IReadOnlyList<ServerMemberDto>>> ListMembers(CancellationToken ct)
    {
        try
        {
            await RequireManageMembersAsync(this.RequireUserId(), ct);
            return Ok(await _roles.ListMembersAsync(ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("members/{userId:guid}/roles")]
    public async Task<IActionResult> AssignRoles(Guid userId, AssignServerRolesRequest request, CancellationToken ct)
    {
        try
        {
            await RequireManageMembersAsync(this.RequireUserId(), ct);
            await _roles.AssignRolesAsync(userId, request.RoleIds, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("members/by-username/{username}/roles")]
    public async Task<IActionResult> AssignRolesByUsername(string username, AssignServerRolesRequest request, CancellationToken ct)
    {
        try
        {
            await RequireManageMembersAsync(this.RequireUserId(), ct);
            await _roles.AssignRolesByUsernameAsync(username, request.RoleIds, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }
}
