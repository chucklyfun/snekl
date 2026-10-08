using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Security;
using Snekl.Account.Infrastructure.Services;
using Snekl.Domain.Dtos;

namespace Snekl.Account.Api.Controllers;

/// <summary>
/// Server-to-server business hosting channel. Callers are content hosts listed under
/// <c>BusinessHosting:TrustedContentServers</c>; every request is a <see cref="FederationEnvelope"/>
/// signed with the host's server key. There is no user token here: the signature is the credential.
/// </summary>
[ApiController]
[Route("api/business/hosting")]
[AllowAnonymous]
public class BusinessHostingController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IBusinessHostingRequestVerifier _verifier;
    private readonly IBusinessAccountService _business;

    public BusinessHostingController(IBusinessHostingRequestVerifier verifier, IBusinessAccountService business)
    {
        _verifier = verifier;
        _business = business;
    }

    /// <summary>Approval status and linked server for a member DID.</summary>
    [HttpPost("status")]
    public async Task<ActionResult<BusinessHostingStatus>> Status(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement body,
        CancellationToken ct)
    {
        var verified = await _verifier.VerifyAsync<BusinessHostingStatusQuery>(ReadEnvelope(body), BusinessHostingEnvelopeTypes.Status, ct);
        if (!verified.IsValid)
            return Rejected(verified.Rejection, verified.Error);

        var query = verified.Payload!;
        var status = await _business.GetHostingStatusAsync(query.MemberDid, query.BusinessAccountId, ct);
        return status is null
            ? NotFound(new { error = "No business account for that member.", code = "no_business" })
            : Ok(status);
    }

    /// <summary>Links the server a content host created to the business account. Safe to retry.</summary>
    [HttpPost("link")]
    public async Task<ActionResult<BusinessHostingLinkResult>> Link(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement body,
        CancellationToken ct)
    {
        var verified = await _verifier.VerifyAsync<BusinessHostingLinkCommand>(ReadEnvelope(body), BusinessHostingEnvelopeTypes.Link, ct);
        if (!verified.IsValid)
            return Rejected(verified.Rejection, verified.Error);

        BusinessServerLinkResult result;
        try
        {
            result = await _business.LinkServerAsync(verified.Payload!, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message, code = "invalid" });
        }

        return result.Outcome switch
        {
            BusinessServerLinkOutcome.Linked => Ok(new BusinessHostingLinkResult(result.Business!, true)),
            BusinessServerLinkOutcome.AlreadyLinked => Ok(new BusinessHostingLinkResult(result.Business!, false)),
            BusinessServerLinkOutcome.NotFound => NotFound(new { error = result.Error, code = "no_business" }),
            BusinessServerLinkOutcome.NotApproved => Conflict(new
            {
                error = result.Error,
                code = "not_approved",
                status = result.Business?.Status
            }),
            BusinessServerLinkOutcome.LinkedToOtherServer => Conflict(new
            {
                error = result.Error,
                code = "already_has_server",
                serverId = result.Business?.ServerId,
                serverDid = result.Business?.ServerDid
            }),
            BusinessServerLinkOutcome.ServerInUse => Conflict(new { error = result.Error, code = "server_in_use" }),
            _ => throw new InvalidOperationException($"Unexpected link outcome {result.Outcome}.")
        };
    }

    /// <summary>
    /// Read the envelope by hand so an empty or partial body is reported as an unsigned request (401)
    /// rather than as an MVC model validation error.
    /// </summary>
    private static FederationEnvelope? ReadEnvelope(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return null;
        try
        {
            return body.Deserialize<FederationEnvelope>(JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private ObjectResult Rejected(BusinessHostingRejection rejection, string? error)
    {
        var status = rejection switch
        {
            BusinessHostingRejection.WrongType or BusinessHostingRejection.Malformed => StatusCodes.Status400BadRequest,
            BusinessHostingRejection.UntrustedSigner => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status401Unauthorized
        };
        return StatusCode(status, new { error, code = RejectionCode(rejection) });
    }

    public static string RejectionCode(BusinessHostingRejection rejection) => rejection switch
    {
        BusinessHostingRejection.Missing => "signature_required",
        BusinessHostingRejection.WrongType => "wrong_type",
        BusinessHostingRejection.Stale => "stale",
        BusinessHostingRejection.Malformed => "malformed",
        BusinessHostingRejection.UntrustedSigner => "untrusted_signer",
        BusinessHostingRejection.KeyUnavailable => "key_unavailable",
        BusinessHostingRejection.BadSignature => "bad_signature",
        BusinessHostingRejection.WrongAudience => "wrong_audience",
        BusinessHostingRejection.Replayed => "replayed",
        _ => "rejected"
    };
}
