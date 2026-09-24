using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Security;
using Snekl.Account.Infrastructure.Services;

namespace Snekl.Account.Api.Controllers;

[ApiController]
[Route("api/auth/admin")]
public class AuthAdminController : ControllerBase
{
    private readonly IAuthOperatorService _operators;

    public AuthAdminController(IAuthOperatorService operators) => _operators = operators;

    [HttpGet("status")]
    [Authorize]
    public async Task<ActionResult<AuthOperatorStatusDto>> Status(CancellationToken ct)
    {
        var did = User.FindFirstValue("did");
        if (string.IsNullOrWhiteSpace(did))
            return Unauthorized(new { error = "Member token is missing a did claim." });
        var username = User.FindFirstValue("username");
        return Ok(await _operators.GetStatusAsync(did, username, ct));
    }

    [HttpPost("login")]
    [Authorize]
    public async Task<ActionResult<AuthAdminAuthResponse>> Login(
        [FromBody] AuthOperatorLoginRequest request,
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
            var status = await _operators.GetStatusAsync(did, username, ct);
            var code = status.State == AuthOperatorStates.Forbidden
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
    [Authorize(AuthenticationSchemes = AuthAdminAuth.Scheme, Policy = AuthAdminAuth.Policy)]
    public async Task<ActionResult<IReadOnlyList<AuthOperatorDto>>> List(CancellationToken ct) =>
        Ok(await _operators.ListAsync(ct));

    [HttpPost("operators")]
    [Authorize(AuthenticationSchemes = AuthAdminAuth.Scheme, Policy = AuthAdminAuth.Policy)]
    public async Task<ActionResult<AuthOperatorDto>> Invite(
        [FromBody] InviteAuthOperatorRequest request,
        CancellationToken ct)
    {
        try
        {
            var idValue = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(idValue) || !Guid.TryParse(idValue, out var id))
                return Unauthorized();
            return Ok(await _operators.InviteAsync(id, request.Username, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("operators/{operatorId:guid}")]
    [Authorize(AuthenticationSchemes = AuthAdminAuth.Scheme, Policy = AuthAdminAuth.Policy)]
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
}

[ApiController]
[Route("api/auth")]
public class AuthServerInfoController : ControllerBase
{
    private readonly IAuthServerService _server;

    public AuthServerInfoController(IAuthServerService server) => _server = server;

    [HttpGet("server")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthServerInfoDto>> Get(CancellationToken ct)
    {
        var config = await _server.GetOrCreateAsync(ct);
        return Ok(new AuthServerInfoDto(
            config.Name,
            config.BaseUrl,
            config.Did,
            $"{config.BaseUrl.TrimEnd('/')}/.well-known/jwks.json"));
    }
}

[ApiController]
[Route("admin")]
public class AuthAdminBootstrapController : ControllerBase
{
    private readonly IAuthServerService _server;

    public AuthAdminBootstrapController(IAuthServerService server) => _server = server;

    [HttpGet("bootstrap")]
    [AllowAnonymous]
    public async Task<ActionResult<AdminBootstrapDto>> Bootstrap(CancellationToken ct)
    {
        var config = await _server.GetOrCreateAsync(ct);
        return Ok(new AdminBootstrapDto("auth", config.Name, config.BaseUrl, config.Did, config.BaseUrl));
    }
}
