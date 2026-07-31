using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Services;

namespace Snekl.Account.Api.Controllers;

[ApiController]
[Route("api/me")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly IPersonClusterService _clusters;

    public MeController(IPersonClusterService clusters) => _clusters = clusters;

    [HttpGet]
    public async Task<ActionResult<PersonClusterDto>> Get(CancellationToken ct)
    {
        var clusterId = Guid.Parse(User.FindFirstValue("cluster_id")!);
        var cluster = await _clusters.GetAsync(clusterId, ct);
        return cluster is null ? NotFound() : Ok(cluster);
    }

    [HttpPut("profile")]
    public async Task<ActionResult<PersonClusterDto>> UpdateProfile(UpdateProfileRequest request, CancellationToken ct)
    {
        var clusterId = Guid.Parse(User.FindFirstValue("cluster_id")!);
        try
        {
            return Ok(await _clusters.UpdateProfileAsync(clusterId, request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
