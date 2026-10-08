using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Security;
using Snekl.Account.Infrastructure.Services;

namespace Snekl.Account.Api.Controllers;

[ApiController]
[Route("api/me/follows")]
public class FollowsController : ControllerBase
{
    private readonly IFollowService _follows;

    public FollowsController(IFollowService follows) => _follows = follows;

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<FollowDto>>> List(CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            return Ok(await _follows.ListAsync(clusterId, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<FollowDto>> Upsert(UpsertFollowRequest request, CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            return Ok(await _follows.UpsertAsync(clusterId, request, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("alert")]
    [Authorize]
    public async Task<ActionResult<FollowDto>> SetAlert(SetFollowAlertRequest request, CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            return Ok(await _follows.SetAlertModeAsync(clusterId, request.Did, request.Mode, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete]
    [Authorize]
    public async Task<IActionResult> Remove([FromQuery] string did, CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            await _follows.RemoveAsync(clusterId, did, ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
