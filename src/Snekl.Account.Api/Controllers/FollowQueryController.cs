using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Services;

namespace Snekl.Account.Api.Controllers;

[ApiController]
[Route("api/follows")]
public class FollowQueryController : ControllerBase
{
    private readonly IFollowService _follows;

    public FollowQueryController(IFollowService follows) => _follows = follows;

    [HttpGet("contains")]
    public async Task<ActionResult<FollowContainsDto>> Contains(
        [FromQuery] string followerDid,
        [FromQuery] string targetDid,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(followerDid) || string.IsNullOrWhiteSpace(targetDid))
            return BadRequest(new { error = "followerDid and targetDid are required." });

        return Ok(new FollowContainsDto(await _follows.FollowsAsync(followerDid, targetDid, ct)));
    }
}
