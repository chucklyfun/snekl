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
    private readonly INsfwAgeCheckService _nsfwAge;
    private readonly IChangeLogService _changeLog;
    private readonly IOwnershipService _ownership;
    private readonly INodeService _nodes;

    public ServerRolesController(
        IServerRoleService roles,
        IPermissionService permissions,
        INsfwAgeCheckService nsfwAge,
        IChangeLogService changeLog,
        IOwnershipService ownership,
        INodeService nodes)
    {
        _roles = roles;
        _permissions = permissions;
        _nsfwAge = nsfwAge;
        _changeLog = changeLog;
        _ownership = ownership;
        _nodes = nodes;
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
        var isOwner = await _permissions.IsServerOwnerAsync(userId, ct);
        var perms = await _permissions.GetServerPermissionsAsync(userId, ct);
        var roles = await _roles.ListEffectiveRolesForUserAsync(userId, ct);
        var age = await _nsfwAge.GetStatusAsync(userId, null, ct);
        var voteAlertBase = await _nodes.GetVoteAlertBaseAsync(ct);
        return Ok(new ServerPermissionsDto(
            isSuper,
            PermissionNames.ToNames(perms),
            roles,
            age.RequiredByServer,
            age.MinimumAge,
            age.ViewerAttested,
            isOwner,
            voteAlertBase));
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("vote-alerts")]
    public async Task<ActionResult<VoteAlertBaseDto>> UpdateVoteAlerts(
        UpdateVoteAlertBaseRequest request,
        CancellationToken ct)
    {
        try
        {
            var userId = this.RequireUserId();
            if (!await _permissions.IsSuperUserAsync(userId, ct))
                await _permissions.RequireServerPermissionAsync(userId, ServerPermissions.ManageServerSettings, ct);
            var alertBase = await _nodes.SetVoteAlertBaseAsync(request.Base, ct);
            return Ok(new VoteAlertBaseDto(alertBase));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("transfer-ownership")]
    public async Task<ActionResult<NodeInfoDto>> TransferOwnership(
        TransferOwnershipRequest request,
        CancellationToken ct)
    {
        if (RecentPasswordAuth.RejectIfStale(this) is { } rejected)
            return rejected;

        try
        {
            return Ok(await _ownership.TransferServerOwnershipAsync(this.RequireUserId(), request.Username, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("nsfw-age-check")]
    public async Task<ActionResult<NsfwAgeCheckStatusDto>> UpdateNsfwAgeCheck(
        UpdateServerNsfwAgeCheckRequest request,
        CancellationToken ct)
    {
        try
        {
            var userId = this.RequireUserId();
            if (!await _permissions.IsSuperUserAsync(userId, ct))
                await _permissions.RequireServerPermissionAsync(userId, ServerPermissions.ManageServerSettings, ct);
            await _nsfwAge.UpdateServerPolicyAsync(request.Required, request.MinimumAge, userId, ct);
            return Ok(await _nsfwAge.GetStatusAsync(userId, null, ct));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
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
            return Ok(await _roles.CreateRoleAsync(request, this.RequireUserId(), ct));
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
            return Ok(await _roles.UpdateRoleAsync(roleId, request, this.RequireUserId(), ct));
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
            await _roles.DeleteRoleAsync(roleId, this.RequireUserId(), ct);
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
            await _roles.AssignRolesAsync(userId, request.RoleIds, this.RequireUserId(), ct);
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
            await _roles.AssignRolesByUsernameAsync(username, request.RoleIds, this.RequireUserId(), ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<ChangeLogEntryDto>>> ListHistory(CancellationToken ct)
    {
        try
        {
            var userId = this.RequireUserId();
            await _permissions.RequireServerPermissionAsync(userId, ServerPermissions.ViewHistory, ct);
            return Ok(await _changeLog.ListServerAsync(100, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }
}
