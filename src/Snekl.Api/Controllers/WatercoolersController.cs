using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/watercoolers")]
public class WatercoolersController : ControllerBase
{
    private readonly IWatercoolerService _coolers;
    private readonly IForumService _forums;
    private readonly IChangeLogService _changeLog;
    private readonly IPermissionService _permissions;

    public WatercoolersController(
        IWatercoolerService coolers,
        IForumService forums,
        IChangeLogService changeLog,
        IPermissionService permissions)
    {
        _coolers = coolers;
        _forums = forums;
        _changeLog = changeLog;
        _permissions = permissions;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WatercoolerDto>>> List(CancellationToken ct)
    {
        try
        {
            return Ok(await _coolers.ListServerAsync(this.ViewerUserId(), ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<WatercoolerDto>> Get(
        string slug,
        [FromQuery] Guid? parentForumId,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var parentId = parentForumId;
            if (!parentId.HasValue && !string.IsNullOrWhiteSpace(parentSlug))
            {
                var parent = await _forums.GetEntityBySlugAsync(parentSlug, null, ct);
                parentId = parent?.Id;
            }

            var cooler = await _coolers.GetBySlugAsync(slug, parentId, this.ViewerUserId(), ct);
            return cooler is null ? NotFound() : Ok(cooler);
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("{slug}/history")]
    public async Task<ActionResult<IReadOnlyList<ChangeLogEntryDto>>> ListHistory(
        string slug,
        [FromQuery] Guid? parentForumId,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var parentId = parentForumId;
            if (!parentId.HasValue && !string.IsNullOrWhiteSpace(parentSlug))
            {
                var parent = await _forums.GetEntityBySlugAsync(parentSlug, null, ct);
                parentId = parent?.Id;
            }

            var existing = await _coolers.GetEntityBySlugAsync(slug, parentId, ct);
            if (existing is null) return NotFound();
            var userId = this.RequireUserId();
            await _permissions.RequireWatercoolerPermissionAsync(userId, existing.Id, WatercoolerPermissions.ViewHistory, ct);
            return Ok(await _changeLog.ListWatercoolerAsync(existing.Id, 100, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost]
    public async Task<ActionResult<WatercoolerDto>> Create(CreateWatercoolerRequest request, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var created = await _coolers.CreateAsync(userId, request, userId, ct);
            return CreatedAtAction(nameof(Get), new { slug = created.Slug, parentForumId = created.ParentForumId }, created);
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("{slug}")]
    public async Task<ActionResult<WatercoolerDto>> Update(
        string slug,
        UpdateWatercoolerRequest request,
        [FromQuery] Guid? parentForumId,
        CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var existing = await _coolers.GetEntityBySlugAsync(slug, parentForumId, ct);
            if (existing is null) return NotFound();
            return Ok(await _coolers.UpdateAsync(userId, existing.Id, request, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("{slug}/approve")]
    public async Task<ActionResult<WatercoolerDto>> Approve(string slug, [FromQuery] Guid? parentForumId, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var existing = await _coolers.GetEntityBySlugAsync(slug, parentForumId, ct);
            if (existing is null) return NotFound();
            return Ok(await _coolers.ApproveAsync(userId, existing.Id, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("{slug}/reject")]
    public async Task<IActionResult> Reject(string slug, [FromQuery] Guid? parentForumId, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var existing = await _coolers.GetEntityBySlugAsync(slug, parentForumId, ct);
            if (existing is null) return NotFound();
            await _coolers.RejectAsync(userId, existing.Id, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpDelete("{slug}")]
    public async Task<IActionResult> Delete(string slug, [FromQuery] Guid? parentForumId, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var existing = await _coolers.GetEntityBySlugAsync(slug, parentForumId, ct);
            if (existing is null) return NotFound();
            await _coolers.DeleteAsync(userId, existing.Id, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("{slug}/roles")]
    public async Task<ActionResult<IReadOnlyList<WatercoolerRoleDto>>> Roles(
        string slug,
        [FromQuery] Guid? parentForumId,
        CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var existing = await _coolers.GetEntityBySlugAsync(slug, parentForumId, ct);
            if (existing is null) return NotFound();
            return Ok(await _coolers.ListRolesAsync(existing.Id, userId, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("{slug}/roles")]
    public async Task<ActionResult<WatercoolerRoleDto>> CreateRole(
        string slug,
        CreateWatercoolerRoleRequest request,
        [FromQuery] Guid? parentForumId,
        CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var existing = await _coolers.GetEntityBySlugAsync(slug, parentForumId, ct);
            if (existing is null) return NotFound();
            return Ok(await _coolers.CreateRoleAsync(existing.Id, userId, request, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("{slug}/roles/{roleId:guid}")]
    public async Task<ActionResult<WatercoolerRoleDto>> UpdateRole(
        string slug,
        Guid roleId,
        UpdateWatercoolerRoleRequest request,
        [FromQuery] Guid? parentForumId,
        CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var existing = await _coolers.GetEntityBySlugAsync(slug, parentForumId, ct);
            if (existing is null) return NotFound();
            return Ok(await _coolers.UpdateRoleAsync(existing.Id, roleId, userId, request, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }
}
