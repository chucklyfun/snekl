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
    private readonly ITwoFactorService _twoFactor;
    private readonly IPasswordResetService _passwordReset;
    private readonly IEmailVerificationService _emailVerification;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public AuthController(
        IAccountAuthService auth,
        ITwoFactorService twoFactor,
        IPasswordResetService passwordReset,
        IEmailVerificationService emailVerification,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _auth = auth;
        _twoFactor = twoFactor;
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
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (!TryDeviceKey(request.DeviceKey, out var thumbprint, out var keyError))
            return BadRequest(new { error = keyError });

        var result = await _auth.LoginAsync(request, thumbprint, ct);
        if (result is not null)
        {
            if (result.RequiresTwoFactor)
            {
                return Unauthorized(new TwoFactorChallengeDto(
                    "Two-factor authentication required.",
                    TwoFactorErrorCodes.Required,
                    result.ChallengeToken!,
                    result.ChallengeExpiresAt ?? DateTime.UtcNow.AddMinutes(5)));
            }

            return Ok(result.Session);
        }

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

    [HttpPost("login/two-factor")]
    public async Task<IActionResult> CompleteTwoFactorLogin(CompleteTwoFactorLoginRequest request, CancellationToken ct)
    {
        var result = await _twoFactor.CompleteLoginAsync(request.ChallengeToken, request.Code, ct);
        return result.Status switch
        {
            TwoFactorLoginStatus.Success => Ok(result.Session),
            TwoFactorLoginStatus.InvalidCode => Unauthorized(new
            {
                error = "Invalid authentication code.",
                code = TwoFactorErrorCodes.Invalid
            }),
            TwoFactorLoginStatus.InvalidChallenge => Unauthorized(new
            {
                error = "That sign-in attempt expired. Enter your password again.",
                code = TwoFactorErrorCodes.Expired
            }),
            _ => throw new InvalidOperationException($"Unexpected two-factor login status {result.Status}.")
        };
    }

    [Authorize]
    [HttpPost("reauthenticate")]
    public async Task<IActionResult> Reauthenticate(ReauthenticateRequest request, CancellationToken ct)
    {
        var identityClaim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (identityClaim is null || !Guid.TryParse(identityClaim, out var identityId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        var currentThumbprint = User.FindFirstValue(OAuthClaims.CnfJkt);
        if (string.IsNullOrWhiteSpace(currentThumbprint)
            && !TryDeviceKey(request.DeviceKey, out currentThumbprint, out var keyError))
            return BadRequest(new { error = keyError });

        var result = await _auth.ReauthenticateAsync(identityId, request.Password, request.TwoFactorCode, currentThumbprint, ct);
        return result.Status switch
        {
            AccountReauthStatus.Success => Ok(result.Session),
            AccountReauthStatus.InvalidPassword => Unauthorized(new { error = "Invalid password." }),
            AccountReauthStatus.TwoFactorRequired => Unauthorized(new
            {
                error = "Two-factor code required.",
                code = TwoFactorErrorCodes.Required
            }),
            AccountReauthStatus.TwoFactorInvalid => Unauthorized(new
            {
                error = "Invalid two-factor code.",
                code = TwoFactorErrorCodes.Invalid
            }),
            _ => throw new InvalidOperationException($"Unexpected reauth status {result.Status}.")
        };
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
