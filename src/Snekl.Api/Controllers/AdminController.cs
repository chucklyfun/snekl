using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Domain.Identity;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminAuthController : ControllerBase
{
    private readonly ISiteAdminAuthService _adminAuth;

    public AdminAuthController(ISiteAdminAuthService adminAuth) => _adminAuth = adminAuth;

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("operator-status")]
    public async Task<ActionResult<OperatorStatusDto>> OperatorStatus(CancellationToken ct) =>
        Ok(await _adminAuth.GetStatusAsync(this.RequireUserId(), ct));

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("login")]
    public async Task<ActionResult<SiteAdminAuthResponse>> Login(OperatorLoginRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _adminAuth.ElevateAsync(this.RequireUserId(), request, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            var status = await _adminAuth.GetStatusAsync(this.RequireUserId(), ct);
            var code = status.State == OperatorStates.Forbidden
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status401Unauthorized;
            return StatusCode(code, new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Policy = SiteAdminAuthorization.SiteAdminPolicy)]
    [HttpGet("me")]
    public async Task<ActionResult<SiteAdminDto>> Me(CancellationToken ct)
    {
        var adminId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var admin = await _adminAuth.GetByIdAsync(adminId, ct);
        return admin is null ? NotFound() : Ok(admin);
    }
}

[ApiController]
[Route("api/admin/site")]
[Authorize(Policy = SiteAdminAuthorization.SiteAdminPolicy)]
public class AdminSiteController : ControllerBase
{
    private readonly INodeService _node;
    private readonly IFederationService _federation;
    private readonly IPersonClusterMappingService _identity;
    private readonly ISuperUserService _superUsers;
    private readonly IHubInviteService _hubInvites;
    private readonly ISiteAdminAuthService _adminAuth;

    public AdminSiteController(
        INodeService node,
        IFederationService federation,
        IPersonClusterMappingService identity,
        ISuperUserService superUsers,
        IHubInviteService hubInvites,
        ISiteAdminAuthService adminAuth)
    {
        _node = node;
        _federation = federation;
        _identity = identity;
        _superUsers = superUsers;
        _hubInvites = hubInvites;
        _adminAuth = adminAuth;
    }

    [HttpGet("node")]
    public async Task<ActionResult<NodeInfoDto>> GetNode(CancellationToken ct) =>
        Ok(await _node.GetInfoAsync(ct));

    [HttpPut("node")]
    public async Task<ActionResult<NodeInfoDto>> UpdateNode(UpdateNodeRequest request, CancellationToken ct)
    {
        await _node.UpdateAsync(request.Name, request.BaseUrl, request.Description, ct);
        return Ok(await _node.GetInfoAsync(ct));
    }

    [HttpGet("peers")]
    public async Task<ActionResult<IReadOnlyList<PeerDto>>> ListPeers(CancellationToken ct) =>
        Ok(await _federation.ListPeersAsync(ct));

    [HttpPost("peers")]
    public async Task<ActionResult<PeerDto>> SubscribePeer(SubscribePeerRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _federation.SubscribeAsync(request.BaseUrl, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("peers/{peerId:guid}")]
    public async Task<IActionResult> UnsubscribePeer(Guid peerId, CancellationToken ct)
    {
        try
        {
            await _federation.UnsubscribeAsync(peerId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("issuers")]
    public async Task<ActionResult<IReadOnlyList<TrustedAuthIssuer>>> ListIssuers(CancellationToken ct) =>
        Ok(await _identity.ListTrustedIssuersAsync(ct));

    [HttpPost("issuers")]
    public async Task<ActionResult<TrustedAuthIssuer>> RegisterIssuer(
        RegisterTrustedIssuerRequest request,
        CancellationToken ct) =>
        Ok(await _identity.RegisterTrustedIssuerAsync(request, ct));

    [HttpGet("super-users")]
    public async Task<ActionResult<IReadOnlyList<SuperUserDto>>> ListSuperUsers(CancellationToken ct) =>
        Ok(await _superUsers.ListAsync(ct));

    [HttpPost("super-users")]
    public async Task<ActionResult<SuperUserDto>> GrantSuperUser(GrantSuperUserRequest request, CancellationToken ct)
    {
        try
        {
            var adminId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _superUsers.GrantAsync(adminId, request.Username, request.Note, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("super-users/{userId:guid}")]
    public async Task<IActionResult> RevokeSuperUser(Guid userId, CancellationToken ct)
    {
        try
        {
            await _superUsers.RevokeAsync(userId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("hub-invites")]
    public async Task<ActionResult<IReadOnlyList<HubInviteDto>>> ListHubInvites(CancellationToken ct) =>
        Ok(await _hubInvites.ListPendingAsync(ct));

    [HttpPost("hub-invites/{inviteId:guid}/accept")]
    public async Task<IActionResult> AcceptHubInvite(Guid inviteId, CancellationToken ct)
    {
        try
        {
            await _hubInvites.AcceptAsync(inviteId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("hub-invites/{inviteId:guid}/decline")]
    public async Task<IActionResult> DeclineHubInvite(Guid inviteId, CancellationToken ct)
    {
        try
        {
            await _hubInvites.DeclineAsync(inviteId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("operators")]
    public async Task<ActionResult<IReadOnlyList<SiteAdminDto>>> ListOperators(CancellationToken ct) =>
        Ok(await _adminAuth.ListAsync(ct));

    [HttpPost("operators")]
    public async Task<ActionResult<SiteAdminDto>> InviteOperator(InviteOperatorRequest request, CancellationToken ct)
    {
        try
        {
            var adminId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _adminAuth.InviteAsync(adminId, request.Username, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("operators/{operatorId:guid}")]
    public async Task<IActionResult> RevokeOperator(Guid operatorId, CancellationToken ct)
    {
        try
        {
            await _adminAuth.RevokeAsync(operatorId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
