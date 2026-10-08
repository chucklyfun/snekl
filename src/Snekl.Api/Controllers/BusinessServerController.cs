using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

/// <summary>
/// Lets the owner of an approved business account create their one hosted server on this content host.
/// Approval and the server link live on the member's Auth; this host asks and reports over a signed
/// server-to-server channel (see <see cref="IBusinessHostingAuthClient"/>).
/// </summary>
[ApiController]
[Route("api/host/business-server")]
[Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
public class BusinessServerController : ControllerBase
{
    private readonly IBusinessServerService _service;

    public BusinessServerController(IBusinessServerService service) => _service = service;

    public sealed record CreateBusinessServerRequest(Guid? BusinessAccountId = null);

    public sealed record BusinessServerResponse(
        Guid ServerId,
        string ServerDid,
        string Name,
        string BaseUrl,
        Guid? BusinessAccountId,
        bool Created);

    [HttpPost]
    public async Task<ActionResult<BusinessServerResponse>> Create(
        [FromBody] CreateBusinessServerRequest? request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized(new { error = "Invalid session. Please log in again.", code = "not_signed_in" });
        var did = User.FindFirstValue("did") ?? string.Empty;

        BusinessServerResult result;
        try
        {
            result = await _service.CreateAsync(userId, did, request?.BusinessAccountId, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message, code = "invalid" });
        }

        return result.Outcome switch
        {
            BusinessServerOutcome.Created => StatusCode(StatusCodes.Status201Created, ToResponse(result, true)),
            BusinessServerOutcome.Relinked => Ok(ToResponse(result, false)),
            BusinessServerOutcome.NotSignedIn => Unauthorized(new { error = result.Error, code = "not_signed_in" }),
            BusinessServerOutcome.NoBusiness => NotFound(new { error = result.Error, code = "no_business" }),
            BusinessServerOutcome.NotApproved => StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = result.Error,
                code = "not_approved",
                status = result.BusinessStatus
            }),
            BusinessServerOutcome.AlreadyHasServer => Conflict(new
            {
                error = result.Error,
                code = "already_has_server",
                serverId = result.LinkedServerId,
                serverDid = result.LinkedServerDid
            }),
            BusinessServerOutcome.AuthUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = result.Error,
                code = "auth_unavailable"
            }),
            BusinessServerOutcome.AuthRejected => StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = result.Error,
                code = "auth_rejected"
            }),
            BusinessServerOutcome.LinkFailed => StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = result.Error,
                code = "link_failed",
                serverId = result.Server?.Id,
                serverDid = result.Server?.Did
            }),
            _ => throw new InvalidOperationException($"Unexpected outcome {result.Outcome}.")
        };
    }

    private static BusinessServerResponse ToResponse(BusinessServerResult result, bool created) =>
        new(
            result.Server!.Id,
            result.Server.Did,
            result.Server.Name,
            result.Server.BaseUrl,
            result.BusinessAccountId,
            created);
}
