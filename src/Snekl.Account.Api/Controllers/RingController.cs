using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Services;

namespace Snekl.Account.Api.Controllers;

[ApiController]
[Route("api/ring")]
public class RingController : ControllerBase
{
    private readonly IPartnerService _partners;

    public RingController(IPartnerService partners) => _partners = partners;

    [HttpPost("session")]
    public async Task<ActionResult<PartnerSessionDto>> CreateSession(VouchPayload vouch, CancellationToken ct)
    {
        var session = await _partners.AcceptRingSessionAsync(vouch, ct);
        return session is null ? Unauthorized(new { error = "Invalid or untrusted ring session request." }) : Ok(session);
    }
}
