using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Hub.Domain.Dtos;
using Snekl.Hub.Infrastructure.Security;
using Snekl.Hub.Infrastructure.Services;

namespace Snekl.Hub.Controllers;

[ApiController]
[Route("api/hub/admin")]
public class HubAdminController : ControllerBase
{
    private readonly IHubOperatorService _operators;
    private readonly IHubFeedOriginSync _feedSync;

    public HubAdminController(IHubOperatorService operators, IHubFeedOriginSync feedSync)
    {
        _operators = operators;
        _feedSync = feedSync;
    }

    [HttpGet("status")]
    [Authorize]
    public async Task<ActionResult<HubOperatorStatusDto>> Status(CancellationToken ct)
    {
        var did = User.FindFirstValue("did");
        if (string.IsNullOrWhiteSpace(did))
            return Unauthorized(new { error = "Member token is missing a did claim." });
        var username = User.FindFirstValue("username");
        return Ok(await _operators.GetStatusAsync(did, username, ct));
    }

    [HttpPost("login")]
    [Authorize]
    public async Task<ActionResult<HubAdminAuthResponse>> Login(
        [FromBody] HubOperatorLoginRequest request,
        CancellationToken ct)
    {
        var did = User.FindFirstValue("did");
        if (string.IsNullOrWhiteSpace(did))
            return Unauthorized(new { error = "Member token is missing a did claim." });
        var username = User.FindFirstValue("username") ?? did;

        try
        {
            return Ok(await _operators.ElevateAsync(did, username, request, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            var status = await _operators.GetStatusAsync(did, ct);
            var code = status.State == HubOperatorStates.Forbidden
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status401Unauthorized;
            return StatusCode(code, new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("operators")]
    [Authorize(AuthenticationSchemes = HubAdminAuth.Scheme, Policy = HubAdminAuth.Policy)]
    public async Task<ActionResult<IReadOnlyList<HubOperatorDto>>> List(CancellationToken ct) =>
        Ok(await _operators.ListAsync(ct));

    [HttpPost("operators")]
    [Authorize(AuthenticationSchemes = HubAdminAuth.Scheme, Policy = HubAdminAuth.Policy)]
    public async Task<ActionResult<HubOperatorDto>> Invite(
        [FromBody] InviteHubOperatorRequest request,
        CancellationToken ct)
    {
        try
        {
            var idValue = User.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(idValue) || !Guid.TryParse(idValue, out var id))
                return Unauthorized();
            return Ok(await _operators.InviteAsync(id, request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("operators/{operatorId:guid}")]
    [Authorize(AuthenticationSchemes = HubAdminAuth.Scheme, Policy = HubAdminAuth.Policy)]
    public async Task<IActionResult> Revoke(Guid operatorId, CancellationToken ct)
    {
        try
        {
            await _operators.RevokeAsync(operatorId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("feed/sync")]
    [Authorize(AuthenticationSchemes = HubAdminAuth.Scheme, Policy = HubAdminAuth.Policy)]
    public async Task<ActionResult<HubFeedSyncResultDto>> SyncFeed(CancellationToken ct) =>
        Ok(await _feedSync.SyncAsync(ct, maxAttempts: 1));
}
