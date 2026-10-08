using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Security;
using Snekl.Account.Infrastructure.Services;
using Snekl.Domain.Security;

namespace Snekl.Account.Api.Controllers;

/// <summary>
/// Business accounts from the public business page (snekl-landing). Signup is anonymous and creates
/// the contact's Snekl Account plus a pending business account. Auth operators list, approve, and
/// reject signups. Approval does not create a server; an approved owner creates it themselves.
/// </summary>
[ApiController]
[Route("api/business")]
public class BusinessAccountsController : ControllerBase
{
    private readonly IBusinessAccountService _business;

    public BusinessAccountsController(IBusinessAccountService business) => _business = business;

    [HttpGet("services")]
    public ActionResult<IReadOnlyList<string>> Services() => Ok(BusinessServiceKeys.All);

    [HttpPost("signup")]
    public async Task<ActionResult<BusinessAccountDto>> SignUp(BusinessSignupRequest request, CancellationToken ct)
    {
        if (!DeviceKeyReader.TryRead(
                request.DeviceKey,
                Request.Headers[DeviceProof.DeviceKeyHeaderName].ToString(),
                out var thumbprint,
                out var keyError))
            return BadRequest(new { error = keyError });

        try
        {
            return Ok(await _business.SignUpAsync(request, thumbprint, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("accounts")]
    [Authorize(AuthenticationSchemes = AuthAdminAuth.Scheme, Policy = AuthAdminAuth.Policy)]
    public async Task<ActionResult<IReadOnlyList<BusinessAccountDto>>> List(CancellationToken ct) =>
        Ok(await _business.ListAsync(ct));

    [HttpPost("accounts/{id:guid}/approve")]
    [Authorize(AuthenticationSchemes = AuthAdminAuth.Scheme, Policy = AuthAdminAuth.Policy)]
    public async Task<ActionResult<BusinessAccountDto>> Approve(Guid id, CancellationToken ct) =>
        ToActionResult(await _business.ApproveAsync(id, Reviewer(), ct));

    [HttpPost("accounts/{id:guid}/reject")]
    [Authorize(AuthenticationSchemes = AuthAdminAuth.Scheme, Policy = AuthAdminAuth.Policy)]
    public async Task<ActionResult<BusinessAccountDto>> Reject(Guid id, CancellationToken ct) =>
        ToActionResult(await _business.RejectAsync(id, Reviewer(), ct));

    /// <summary>The signed-in member's business accounts and their review status (for the dashboard).</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<BusinessAccountDto>>> Mine(CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });
        return Ok(await _business.ListForPersonAsync(clusterId, ct));
    }

    private string Reviewer() =>
        User.FindFirstValue("username") ?? User.FindFirstValue("did") ?? "operator";

    private ActionResult<BusinessAccountDto> ToActionResult(BusinessReviewResult result) =>
        result.Outcome switch
        {
            BusinessReviewOutcome.Changed or BusinessReviewOutcome.Unchanged => Ok(result.Account),
            BusinessReviewOutcome.NotFound => NotFound(new { error = result.Error }),
            BusinessReviewOutcome.Conflict => Conflict(new { error = result.Error, status = result.Account?.Status }),
            _ => throw new InvalidOperationException($"Unexpected review outcome {result.Outcome}.")
        };
}
