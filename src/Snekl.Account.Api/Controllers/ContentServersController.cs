using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Services;

namespace Snekl.Account.Api.Controllers;

[ApiController]
[Route("api/me/content-servers")]
public class ContentServersController : ControllerBase
{
    private readonly IContentServerMembershipService _memberships;

    public ContentServersController(IContentServerMembershipService memberships) => _memberships = memberships;

    [HttpGet("presets")]
    [AllowAnonymous]
    public ActionResult<IReadOnlyList<ContentServerStatusPresetInfo>> GetPresets() =>
        Ok(_memberships.GetPresets());

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<ContentServerMembershipDto>>> List(CancellationToken ct)
    {
        var clusterId = Guid.Parse(User.FindFirstValue("cluster_id")!);
        return Ok(await _memberships.ListAsync(clusterId, ct));
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ContentServerMembershipDto>> Subscribe(
        SubscribeContentServerRequest request,
        CancellationToken ct)
    {
        var clusterId = Guid.Parse(User.FindFirstValue("cluster_id")!);
        try
        {
            return Ok(await _memberships.SubscribeAsync(clusterId, request.BaseUrl, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{*serverDid}")]
    [Authorize]
    public async Task<ActionResult<ContentServerMembershipDto>> UpdateStatus(
        string serverDid,
        UpdateContentServerStatusRequest request,
        CancellationToken ct)
    {
        var clusterId = Guid.Parse(User.FindFirstValue("cluster_id")!);
        try
        {
            return Ok(await _memberships.UpdateStatusAsync(clusterId, serverDid, request.Status, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{*serverDid}")]
    [Authorize]
    public async Task<IActionResult> Unsubscribe(string serverDid, CancellationToken ct)
    {
        var clusterId = Guid.Parse(User.FindFirstValue("cluster_id")!);
        try
        {
            await _memberships.UnsubscribeAsync(clusterId, serverDid, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
