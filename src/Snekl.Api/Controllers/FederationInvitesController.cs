using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/federation/invites")]
public class FederationInvitesController : ControllerBase
{
    private readonly IHubInviteService _invites;

    public FederationInvitesController(IHubInviteService invites) => _invites = invites;

    /// <summary>
    /// Hub-initiated invite. Caller must be a member with RequestFederation on this origin.
    /// Works without hub membership — JWT is validated via trusted auth issuers.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    public async Task<ActionResult<HubInviteDto>> RequestInvite(
        CreateHubInviteRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _invites.RequestInviteAsync(this.RequireUserId(), request, ct));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }
}
