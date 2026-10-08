using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Security;
using Snekl.Account.Infrastructure.Services;
using Snekl.Domain;

namespace Snekl.Account.Api.Controllers;

[ApiController]
[Route("api/me")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly IPersonClusterService _clusters;
    private readonly IIdentityCredentialService _credentials;
    private readonly IAccountAuthService _auth;
    private readonly ITokenService _tokens;
    private readonly IAuthServerService _server;
    private readonly IEmailVerificationService _verification;

    public MeController(
        IPersonClusterService clusters,
        IIdentityCredentialService credentials,
        IAccountAuthService auth,
        ITokenService tokens,
        IAuthServerService server,
        IEmailVerificationService verification)
    {
        _clusters = clusters;
        _credentials = credentials;
        _auth = auth;
        _tokens = tokens;
        _server = server;
        _verification = verification;
    }

    [HttpGet]
    public async Task<ActionResult<PersonClusterDto>> Get(CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        var cluster = await _clusters.GetAsync(clusterId, ct);
        return cluster is null
            ? NotFound(new { error = "Account not found. Log out and sign in again." })
            : Ok(cluster);
    }

    [HttpPost("export")]
    public async Task<ActionResult<ExportIdentityResponse>> Export(CancellationToken ct)
    {
        var clusterId = Guid.Parse(User.FindFirstValue("cluster_id")!);
        try
        {
            return Ok(await _credentials.ExportAsync(clusterId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("profile")]
    public async Task<ActionResult<UpdateProfileResponse>> UpdateProfile(UpdateProfileRequest request, CancellationToken ct)
    {
        var clusterId = Guid.Parse(User.FindFirstValue("cluster_id")!);
        var identityId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            var person = await _clusters.UpdateProfileAsync(clusterId, request, ct);
            var identity = await _auth.GetIdentityAsync(identityId, ct)
                ?? throw new InvalidOperationException("Identity not found.");
            var config = await _server.GetOrCreateAsync(ct);
            var cluster = identity.PersonCluster
                ?? throw new InvalidOperationException("Person cluster not found.");
            DateTimeOffset? authTime = null;
            var authTimeClaim = User.FindFirstValue(OAuthClaims.AuthTime);
            if (long.TryParse(authTimeClaim, out var authTimeSeconds))
                authTime = DateTimeOffset.FromUnixTimeSeconds(authTimeSeconds);
            var accessToken = _tokens.IssueToken(
                identity,
                cluster,
                config.BaseUrl,
                new TokenIssueOptions(
                    AuthTime: authTime,
                    DeviceKeyThumbprint: User.FindFirstValue(OAuthClaims.CnfJkt)));
            return Ok(new UpdateProfileResponse(person, accessToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("send-verification-email")]
    public async Task<ActionResult<MessageResponse>> SendVerificationEmail(CancellationToken ct)
    {
        var clusterId = Guid.Parse(User.FindFirstValue("cluster_id")!);
        try
        {
            return Ok(await _verification.ResendAsync(clusterId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
