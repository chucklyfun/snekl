using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Services;

namespace Snekl.Account.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAccountAuthService _auth;

    public AuthController(IAccountAuthService auth) => _auth = auth;

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
}
