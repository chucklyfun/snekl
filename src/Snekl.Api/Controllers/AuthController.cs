using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IUserPresentationService _presentation;
    private readonly IPermissionService _permissions;

    public AuthController(IAuthService auth, IUserPresentationService presentation, IPermissionService permissions)
    {
        _auth = auth;
        _presentation = presentation;
        _permissions = permissions;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
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

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await _auth.LoginAsync(request, ct);
        return result is null ? Unauthorized(new { error = "Invalid credentials." }) : Ok(result);
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("me")]
    public async Task<ActionResult<AuthMeResponse>> Me(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var authSource = User.FindFirstValue("auth_source");

        UserPresentationDto presentation;
        if (authSource == "shadow")
            presentation = await _presentation.GetPresentationAsync(userId, userId, null, ct);
        else
        {
            var user = await _auth.GetUserByIdAsync(userId, ct);
            if (user is null) return NotFound();
            presentation = await _presentation.GetPresentationAsync(userId, userId, null, ct);
        }

        var isSuper = await _permissions.IsSuperUserAsync(userId, ct);
        var serverPerms = await _permissions.GetServerPermissionsAsync(userId, ct);
        return Ok(new AuthMeResponse(presentation, isSuper, PermissionNames.ToNames(serverPerms)));
    }
}
