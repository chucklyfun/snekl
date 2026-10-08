using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Security;
using Snekl.Account.Infrastructure.Services;

namespace Snekl.Account.Api.Controllers;

[ApiController]
[Route("api/me/federations")]
public class FederationsController : ControllerBase
{
    private readonly IFederationMembershipService _federations;

    public FederationsController(IFederationMembershipService federations) => _federations = federations;

    [HttpGet("presets")]
    [AllowAnonymous]
    public ActionResult<IReadOnlyList<ContentServerStatusPresetInfo>> GetPresets() =>
        Ok(_federations.GetPresets());

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<FederationMembershipDto>>> List(CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            return Ok(await _federations.ListAsync(clusterId, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<FederationMembershipDto>> Subscribe(
        SubscribeFederationRequest request,
        CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            return Ok(await _federations.SubscribeAsync(clusterId, request.BaseUrl, ct));
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

    [HttpPut]
    [Authorize]
    public async Task<ActionResult<FederationMembershipDto>> UpdateStatus(
        [FromQuery] string baseUrl,
        UpdateFederationStatusRequest request,
        CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            return Ok(await _federations.UpdateStatusAsync(clusterId, baseUrl, request.Status, ct));
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
    public async Task<IActionResult> Unsubscribe([FromQuery] string baseUrl, CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            await _federations.UnsubscribeAsync(clusterId, baseUrl, ct);
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
