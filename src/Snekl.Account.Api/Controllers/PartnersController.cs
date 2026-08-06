using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Services;

namespace Snekl.Account.Api.Controllers;

[ApiController]
[Route("api/partners")]
public class PartnersController : ControllerBase
{
    private readonly IPartnerService _partners;

    public PartnersController(IPartnerService partners) => _partners = partners;

    [Authorize]
    [HttpPost("trust")]
    public async Task<ActionResult<PartnerTrustDto>> EstablishTrust(EstablishPartnerTrustRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _partners.EstablishTrustAsync(request.PartnerBaseUrl, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("trust/incoming")]
    public async Task<IActionResult> AcknowledgeIncomingTrust(IncomingPartnerTrustRequest request, CancellationToken ct)
    {
        await _partners.AcknowledgeIncomingTrustAsync(request, ct);
        return Ok();
    }

    [Authorize]
    [HttpGet("trust")]
    public async Task<ActionResult<IReadOnlyList<PartnerTrustDto>>> ListTrusts(CancellationToken ct) =>
        Ok(await _partners.ListTrustsAsync(ct));

    [Authorize]
    [HttpPost("provision")]
    public async Task<ActionResult<PartnerSessionDto>> Provision([FromBody] ProvisionBody body, CancellationToken ct)
    {
        try
        {
            return Ok(await _partners.AcceptVouchAsync(body.Vouch, body.Password, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize]
    [HttpPost("provision/remote")]
    public async Task<ActionResult<PartnerSessionDto>> ProvisionOnPartner(ProvisionPartnerRequest request, CancellationToken ct)
    {
        var identityId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            return Ok(await _partners.ProvisionOnPartnerAsync(identityId, request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    public record ProvisionBody(VouchPayload Vouch, string? Password);
}
