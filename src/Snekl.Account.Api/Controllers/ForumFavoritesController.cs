using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Security;
using Snekl.Account.Infrastructure.Services;

namespace Snekl.Account.Api.Controllers;

[ApiController]
[Route("api/me/forum-favorites")]
public class ForumFavoritesController : ControllerBase
{
    private readonly IForumFavoriteService _favorites;

    public ForumFavoritesController(IForumFavoriteService favorites) => _favorites = favorites;

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<ForumFavoriteDto>>> List(CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            return Ok(await _favorites.ListAsync(clusterId, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ForumFavoriteDto>> Upsert(
        UpsertForumFavoriteRequest request,
        CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            return Ok(await _favorites.UpsertAsync(clusterId, request, ct));
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

    [HttpDelete]
    [Authorize]
    public async Task<IActionResult> Remove(
        [FromQuery] string serverDid,
        [FromQuery] string forumId,
        CancellationToken ct)
    {
        if (!User.TryGetClusterId(out var clusterId))
            return Unauthorized(new { error = "Invalid session. Please log in again." });

        try
        {
            await _favorites.RemoveAsync(clusterId, serverDid, forumId, ct);
            return NoContent();
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
