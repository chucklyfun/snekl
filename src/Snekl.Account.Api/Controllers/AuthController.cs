using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Services;

namespace Snekl.Account.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAccountAuthService _auth;
    private readonly IPasswordResetService _passwordReset;
    private readonly IEmailVerificationService _emailVerification;

    public AuthController(
        IAccountAuthService auth,
        IPasswordResetService passwordReset,
        IEmailVerificationService emailVerification)
    {
        _auth = auth;
        _passwordReset = passwordReset;
        _emailVerification = emailVerification;
    }

    [HttpPost("register")]
    public async Task<ActionResult<RingSessionDto>> Register(RegisterRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _auth.RegisterAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("import")]
    public async Task<ActionResult<RingSessionDto>> Import(ImportIdentityRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _auth.ImportAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<RingSessionDto>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await _auth.LoginAsync(request, ct);
        return result is null ? Unauthorized(new { error = "Invalid credentials." }) : Ok(result);
    }

    [HttpPost("forgot-password")]
    public async Task<ActionResult<MessageResponse>> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
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
}
