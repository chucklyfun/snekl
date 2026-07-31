using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserPresentationService _presentation;
    private readonly IUserProfileService _profile;

    public UsersController(IUserPresentationService presentation, IUserProfileService profile)
    {
        _presentation = presentation;
        _profile = profile;
    }

    private Guid? ViewerId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return claim is null ? null : Guid.Parse(claim);
    }

    [HttpGet("{username}")]
    public async Task<ActionResult<UserPresentationDto>> GetProfile(string username, CancellationToken ct)
    {
        var profile = await _presentation.GetPresentationByUsernameAsync(username, ViewerId(), ct);
        return profile is null ? NotFound() : Ok(profile);
    }

    [Authorize]
    [HttpPut("me/flair")]
    public async Task<ActionResult<UserPresentationDto>> UpdateFlair(UpdateUserFlairRequest request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _profile.UpdateFlairAsync(userId, request, ct);
        return Ok(await _presentation.GetPresentationAsync(userId, userId, ct));
    }

    [HttpGet("status/presets")]
    public ActionResult<IReadOnlyList<StatusPresetInfo>> GetPresets() =>
        Ok(_profile.GetStatusPresets());

    [Authorize]
    [HttpPut("me/status")]
    public async Task<ActionResult<UserStatusDto>> SetStatus(SetUserStatusRequest request, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _profile.SetStatusAsync(userId, request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize]
    [HttpDelete("me/status")]
    public async Task<IActionResult> ClearStatus(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _profile.ClearStatusAsync(userId, ct);
        return NoContent();
    }
}
