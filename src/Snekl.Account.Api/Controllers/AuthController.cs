using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Security;
using Snekl.Account.Infrastructure.Services;
using Snekl.Domain;
using Snekl.Domain.Security;

namespace Snekl.Account.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAccountAuthService _auth;
    private readonly IPasswordResetService _passwordReset;
    private readonly IEmailVerificationService _emailVerification;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public AuthController(
        IAccountAuthService auth,
        IPasswordResetService passwordReset,
        IEmailVerificationService emailVerification,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _auth = auth;
        _passwordReset = passwordReset;
        _emailVerification = emailVerification;
        _configuration = configuration;
        _environment = environment;
    }

    [HttpPost("register")]
    public async Task<ActionResult<RingSessionDto>> Register(RegisterRequest request, CancellationToken ct)
    {
        if (!TryDeviceKey(request.DeviceKey, out var thumbprint, out var keyError))
            return BadRequest(new { error = keyError });

        try
        {
            return Ok(await _auth.RegisterAsync(request, thumbprint, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("import")]
    public async Task<ActionResult<RingSessionDto>> Import(ImportIdentityRequest request, CancellationToken ct)
    {
        if (!TryDeviceKey(request.DeviceKey, out var thumbprint, out var keyError))
            return BadRequest(new { error = keyError });

        try
        {
            return Ok(await _auth.ImportAsync(request, thumbprint, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<RingSessionDto>> Login(LoginRequest request, CancellationToken ct)
    {
        if (!TryDeviceKey(request.DeviceKey, out var thumbprint, out var keyError))
            return BadRequest(new { error = keyError });

        var result = await _auth.LoginAsync(request, thumbprint, ct);
        if (result is not null)
            return Ok(result);

        if (_environment.IsDevelopment())
        {
            var email = request.Email.Trim().ToLowerInvariant();
            if (!await _auth.EmailExistsAsync(email, ct))
            {
                return Unauthorized(new
                {
                    error = "No account with that email. Create one at /register. If you used --reset-db or deleted Docker volumes, previous accounts are gone."
                });
            }
        }

        return Unauthorized(new { error = "Invalid credentials." });
    }

    [Authorize]
    [HttpPost("reauthenticate")]
    public async Task<ActionResult<RingSessionDto>> Reauthenticate(ReauthenticateRequest request, CancellationToken ct)
    {
        var identityClaim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (identityClaim is null || !Guid.TryParse(identityClaim, out var identityId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        var currentThumbprint = User.FindFirstValue(OAuthClaims.CnfJkt);
        if (string.IsNullOrWhiteSpace(currentThumbprint)
            && !TryDeviceKey(request.DeviceKey, out currentThumbprint, out var keyError))
            return BadRequest(new { error = keyError });

        var result = await _auth.ReauthenticateAsync(identityId, request.Password, currentThumbprint, ct);
        return result is not null
            ? Ok(result)
            : Unauthorized(new { error = "Invalid password." });
    }

    [HttpPost("forgot-password")]
    public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        return Ok(await _passwordReset.RequestResetAsync(request, ct));
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult<MessageResponse>> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _passwordReset.ResetPasswordAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("verify-email")]
    public async Task<ActionResult<MessageResponse>> VerifyEmail(VerifyEmailRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _emailVerification.VerifyAsync(request.Token, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Development/test only. Set Auth:AllowDevPasswordReset to true (enabled by default in Development).
    /// </summary>
    [HttpPost("dev/reset-password")]
    public async Task<IActionResult> DevResetPassword(DevResetPasswordRequest request, CancellationToken ct)
    {
        if (!_configuration.GetValue("Auth:AllowDevPasswordReset", false))
            return NotFound();

        try
        {
            await _auth.DevResetPasswordAsync(request, ct);
            return Ok(new { message = "Password updated. You can log in with the new password." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private bool TryDeviceKey(DevicePublicJwk? bodyKey, out string thumbprint, out string error) =>
        DeviceKeyReader.TryRead(bodyKey, Request.Headers[DeviceProof.DeviceKeyHeaderName].ToString(), out thumbprint, out error);
}
