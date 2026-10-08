using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Services;

namespace Snekl.Account.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me/two-factor")]
public class TwoFactorController : ControllerBase
{
    private readonly ITwoFactorService _twoFactor;

    public TwoFactorController(ITwoFactorService twoFactor) => _twoFactor = twoFactor;

    [HttpGet]
    public async Task<ActionResult<TwoFactorStatusDto>> Status(CancellationToken ct)
    {
        if (!TryGetIdentityId(out var identityId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        var status = await _twoFactor.GetStatusAsync(identityId, ct);
        return Ok(status);
    }

    [HttpPost("setup")]
    public Task<IActionResult> Setup(CancellationToken ct) =>
        RunAsync(id => _twoFactor.BeginSetupAsync(id, ct));

    [HttpPost("confirm")]
    public Task<IActionResult> Confirm(TwoFactorCodeRequest request, CancellationToken ct) =>
        RunAsync(id => _twoFactor.ConfirmSetupAsync(id, request.Code, ct));

    [HttpPost("disable")]
    public Task<IActionResult> Disable(TwoFactorPasswordCodeRequest request, CancellationToken ct) =>
        RunAsync(async id =>
        {
            await _twoFactor.DisableAsync(id, request.Password, request.Code, ct);
            return new MessageResponse("Two-factor authentication is off.");
        });

    [HttpPost("recovery-codes")]
    public Task<IActionResult> RegenerateRecoveryCodes(TwoFactorPasswordCodeRequest request, CancellationToken ct) =>
        RunAsync(id => _twoFactor.RegenerateRecoveryCodesAsync(id, request.Password, request.Code, ct));

    private async Task<IActionResult> RunAsync<T>(Func<Guid, Task<T>> action)
    {
        if (!TryGetIdentityId(out var identityId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            return Ok(await action(identityId));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private bool TryGetIdentityId(out Guid identityId)
    {
        identityId = default;
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return !string.IsNullOrWhiteSpace(claim) && Guid.TryParse(claim, out identityId);
    }
}
