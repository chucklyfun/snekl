using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Security;
using Snekl.Account.Infrastructure.Services;

namespace Snekl.Account.Api.Controllers;

[ApiController]
[Route("api/oauth")]
public class OauthController : ControllerBase
{
    private readonly IOauthService _oauth;

    public OauthController(IOauthService oauth) => _oauth = oauth;

    [HttpGet("apps/{clientId}")]
    public async Task<ActionResult<OAuthAppDto>> GetApp(string clientId, CancellationToken ct)
    {
        var app = await _oauth.GetPublicAppAsync(clientId, ct);
        return app is null ? NotFound(new { error = "Unknown application." }) : Ok(app);
    }

    [Authorize]
    [HttpPost("apps")]
    public async Task<ActionResult<RegisterOAuthAppResponse>> Register(RegisterOAuthAppRequest request, CancellationToken ct)
    {
        try
        {
            User.TryGetClusterId(out var clusterId);
            return Ok(await _oauth.RegisterAppAsync(clusterId == default ? null : clusterId, request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize]
    [HttpPost("authorize")]
    public async Task<ActionResult<AuthorizeInstallResponse>> Authorize(AuthorizeInstallRequest request, CancellationToken ct)
    {
        try
        {
            var bearer = Request.Headers.Authorization.ToString();
            var token = bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? bearer["Bearer ".Length..].Trim()
                : "";
            return Ok(await _oauth.AuthorizeAsync(User, token, request, ct));
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

    [AllowAnonymous]
    [HttpPost("token")]
    public async Task<ActionResult<OAuthTokenResponse>> Token([FromBody] OAuthTokenRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _oauth.IssueTokenAsync(request, ct));
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

    [Authorize]
    [HttpPost("bots")]
    public async Task<ActionResult<OAuthBotCreatedResponse>> CreateBot(CreateOAuthBotRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _oauth.CreateBotAsync(User, request, ct));
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

    [Authorize]
    [HttpPost("bots/reissue")]
    public async Task<ActionResult<OAuthBotCreatedResponse>> ReissueBot(
        [FromBody] ReissueOAuthBotRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _oauth.ReissueBotAsync(User, request.Username, ct));
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
