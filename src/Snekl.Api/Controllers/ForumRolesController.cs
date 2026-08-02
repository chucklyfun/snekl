using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/forums/{slug}/")]
public class ForumRolesController : ControllerBase
{
    private readonly IForumService _forums;
    private readonly IForumRoleService _roles;
    private readonly IForumMemberService _members;
    private readonly IPermissionService _permissions;

    public ForumRolesController(
        IForumService forums,
        IForumRoleService roles,
        IForumMemberService members,
        IPermissionService permissions)
    {
        _forums = forums;
        _roles = roles;
        _members = members;
        _permissions = permissions;
    }

    private async Task<(Guid ForumId, ForumDto Forum)> ResolveForumAsync(string slug, string? parentSlug, Guid? viewerId, CancellationToken ct)
    {
        var forum = await _forums.GetBySlugAsync(slug, viewerId, parentSlug, ct)
            ?? throw new InvalidOperationException("Forum not found.");
        return (forum.Id, forum);
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("roles")]
    public async Task<ActionResult<IReadOnlyList<ForumRoleDto>>> ListRoles(
        string slug,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var viewerId = this.RequireUserId();
            var (forumId, _) = await ResolveForumAsync(slug, parentSlug, viewerId, ct);
            await _permissions.RequireForumPermissionAsync(viewerId, forumId, ForumPermissions.ManageRoles, ct);
            return Ok(await _roles.ListRolesAsync(forumId, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("roles")]
    public async Task<ActionResult<ForumRoleDto>> CreateRole(
        string slug,
        CreateForumRoleRequest request,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var viewerId = this.RequireUserId();
            var (forumId, _) = await ResolveForumAsync(slug, parentSlug, viewerId, ct);
            await _permissions.RequireForumPermissionAsync(viewerId, forumId, ForumPermissions.ManageRoles, ct);
            return Ok(await _roles.CreateRoleAsync(forumId, request, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("roles/{roleId:guid}")]
    public async Task<ActionResult<ForumRoleDto>> UpdateRole(
        string slug,
        Guid roleId,
        UpdateForumRoleRequest request,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var viewerId = this.RequireUserId();
            var (forumId, _) = await ResolveForumAsync(slug, parentSlug, viewerId, ct);
            await _permissions.RequireForumPermissionAsync(viewerId, forumId, ForumPermissions.ManageRoles, ct);
            return Ok(await _roles.UpdateRoleAsync(forumId, roleId, request, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpDelete("roles/{roleId:guid}")]
    public async Task<IActionResult> DeleteRole(
        string slug,
        Guid roleId,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var viewerId = this.RequireUserId();
            var (forumId, _) = await ResolveForumAsync(slug, parentSlug, viewerId, ct);
            await _permissions.RequireForumPermissionAsync(viewerId, forumId, ForumPermissions.ManageRoles, ct);
            await _roles.DeleteRoleAsync(forumId, roleId, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("members")]
    public async Task<ActionResult<IReadOnlyList<ForumMemberDto>>> ListMembers(
        string slug,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var viewerId = this.RequireUserId();
            var (forumId, _) = await ResolveForumAsync(slug, parentSlug, viewerId, ct);
            await _permissions.RequireForumPermissionAsync(viewerId, forumId, ForumPermissions.ManageMembers, ct);
            return Ok(await _roles.ListMembersAsync(forumId, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("members/{userId:guid}/roles")]
    public async Task<IActionResult> AssignRoles(
        string slug,
        Guid userId,
        AssignForumRolesRequest request,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var viewerId = this.RequireUserId();
            var (forumId, _) = await ResolveForumAsync(slug, parentSlug, viewerId, ct);
            await _permissions.RequireForumPermissionAsync(viewerId, forumId, ForumPermissions.ManageMembers, ct);
            await _roles.AssignRolesAsync(forumId, userId, request.RoleIds, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("flair-templates")]
    public async Task<ActionResult<IReadOnlyList<ForumFlairTemplateDto>>> ListFlairTemplates(
        string slug,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var viewerId = this.RequireUserId();
            var (forumId, _) = await ResolveForumAsync(slug, parentSlug, viewerId, ct);
            return Ok(await _members.ListFlairTemplatesAsync(forumId, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("flair-templates")]
    public async Task<ActionResult<ForumFlairTemplateDto>> CreateFlairTemplate(
        string slug,
        CreateForumFlairTemplateRequest request,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var viewerId = this.RequireUserId();
            var (forumId, _) = await ResolveForumAsync(slug, parentSlug, viewerId, ct);
            await _permissions.RequireForumPermissionAsync(viewerId, forumId, ForumPermissions.ManageMembers, ct);
            return Ok(await _members.CreateFlairTemplateAsync(forumId, request, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpDelete("flair-templates/{templateId:guid}")]
    public async Task<IActionResult> DeleteFlairTemplate(
        string slug,
        Guid templateId,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var viewerId = this.RequireUserId();
            var (forumId, _) = await ResolveForumAsync(slug, parentSlug, viewerId, ct);
            await _permissions.RequireForumPermissionAsync(viewerId, forumId, ForumPermissions.ManageMembers, ct);
            await _members.DeleteFlairTemplateAsync(forumId, templateId, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("members/{userId:guid}/flair")]
    public async Task<IActionResult> AssignFlair(
        string slug,
        Guid userId,
        AssignForumFlairRequest request,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var viewerId = this.RequireUserId();
            var (forumId, forum) = await ResolveForumAsync(slug, parentSlug, viewerId, ct);
            if (userId != viewerId)
                await _permissions.RequireForumPermissionAsync(viewerId, forumId, ForumPermissions.ManageMembers, ct);
            await _members.AssignFlairAsync(forumId, userId, request.TemplateId, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("members/me/alias")]
    public async Task<IActionResult> SetMyAlias(
        string slug,
        SetForumAliasRequest request,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var viewerId = this.RequireUserId();
            var (forumId, forum) = await ResolveForumAsync(slug, parentSlug, viewerId, ct);
            if (!forum.AllowAliases)
                return BadRequest(new { error = "Aliases are not allowed in this forum." });
            await _members.SetAliasAsync(forumId, viewerId, request.Alias, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpDelete("members/me/alias")]
    public async Task<IActionResult> RemoveMyAlias(
        string slug,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var viewerId = this.RequireUserId();
            var (forumId, _) = await ResolveForumAsync(slug, parentSlug, viewerId, ct);
            await _members.RemoveAliasAsync(forumId, viewerId, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(
        string slug,
        UpdateForumSettingsRequest request,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var viewerId = this.RequireUserId();
            var (forumId, _) = await ResolveForumAsync(slug, parentSlug, viewerId, ct);
            await _permissions.RequireForumPermissionAsync(viewerId, forumId, ForumPermissions.ManageForum, ct);
            await _members.UpdateForumSettingsAsync(forumId, request, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }
}
