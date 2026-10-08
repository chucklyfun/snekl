using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Security;
using Snekl.Account.Infrastructure.Services;

namespace Snekl.Account.Api.Controllers;

[ApiController]
[Route("api/me/content-servers")]
public class ContentServersController : ControllerBase
{
    private readonly IContentServerMembershipService _memberships;

    public ContentServersController(IContentServerMembershipService memberships) => _memberships = memberships;

    [HttpGet("presets")]
    [AllowAnonymous]
    public ActionResult<IReadOnlyList<ContentServerStatusPresetInfo>> GetPresets() =>
        Ok(_memberships.GetPresets());

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<ContentServerMembershipDto>>> List(CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            return Ok(await _memberships.ListAsync(clusterId, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ContentServerMembershipDto>> Subscribe(
        SubscribeContentServerRequest request,
        CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            return Ok(await _memberships.SubscribeAsync(clusterId, request.BaseUrl, ct));
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

    [HttpPut("{*serverDid}")]
    [Authorize]
    public async Task<ActionResult<ContentServerMembershipDto>> UpdateStatus(
        string serverDid,
        UpdateContentServerStatusRequest request,
        CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            return Ok(await _memberships.UpdateStatusAsync(clusterId, serverDid, request.Status, ct));
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

    [HttpDelete("{*serverDid}")]
    [Authorize]
    public async Task<IActionResult> Unsubscribe(string serverDid, CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            await _memberships.UnsubscribeAsync(clusterId, serverDid, ct);
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
