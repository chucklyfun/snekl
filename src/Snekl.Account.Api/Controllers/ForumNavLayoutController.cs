using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Security;
using Snekl.Account.Infrastructure.Services;

namespace Snekl.Account.Api.Controllers;

[ApiController]
[Route("api/me/forum-groups")]
public class ForumNavLayoutController : ControllerBase
{
    private readonly IForumNavLayoutService _layout;

    public ForumNavLayoutController(IForumNavLayoutService layout) => _layout = layout;

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<ForumNavLayoutDto>> Get(CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            return Ok(await _layout.GetAsync(clusterId, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpPut]
    [Authorize]
    public async Task<ActionResult<ForumNavLayoutDto>> Save(
        ForumNavLayoutDto layout,
        CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            return Ok(await _layout.SaveAsync(clusterId, layout, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
