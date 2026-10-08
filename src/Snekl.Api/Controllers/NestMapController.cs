using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
[Route("api/nest-map")]
public class NestMapController : ControllerBase
{
    private readonly INestMapService _nests;

    public NestMapController(INestMapService nests) => _nests = nests;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NestMapNodeDto>>> List(CancellationToken ct)
    {
        try
        {
            return Ok(await _nests.ListAsync(this.RequireUserId(), ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [HttpPost("{forumId:guid}/place")]
    public async Task<IActionResult> Place(Guid forumId, PlaceNestRequest request, CancellationToken ct)
    {
        try
        {
            await _nests.PlaceAsync(this.RequireUserId(), forumId, request, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [HttpPost("{forumId:guid}/lock")]
    public async Task<IActionResult> Lock(Guid forumId, SetNestFlagRequest request, CancellationToken ct)
    {
        try
        {
            await _nests.SetLockedAsync(this.RequireUserId(), forumId, request.Value, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [HttpPost("{forumId:guid}/hide")]
    public async Task<IActionResult> Hide(Guid forumId, SetNestFlagRequest request, CancellationToken ct)
    {
        try
        {
            await _nests.SetHiddenAsync(this.RequireUserId(), forumId, request.Value, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [HttpPost("{forumId:guid}/purge")]
    public async Task<IActionResult> Purge(Guid forumId, CancellationToken ct)
    {
        try
        {
            await _nests.PurgeAsync(this.RequireUserId(), forumId, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }
}
