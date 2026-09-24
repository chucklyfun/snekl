using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/forums")]
public class ForumsController : ControllerBase
{
    private readonly IForumService _forums;
    private readonly IWatercoolerService _watercoolers;
    private readonly IFederationService _federation;
    private readonly IAuthService _auth;
    private readonly IHubFeedPublisher _hubFeed;

    public ForumsController(
        IForumService forums,
        IWatercoolerService watercoolers,
        IFederationService federation,
        IAuthService auth,
        IHubFeedPublisher hubFeed)
    {
        _forums = forums;
        _watercoolers = watercoolers;
        _federation = federation;
        _auth = auth;
        _hubFeed = hubFeed;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ForumDto>>> List(CancellationToken ct)
    {
        try
        {
            return Ok(await _forums.ListAsync(this.ViewerUserId(), ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<ForumDto>> Get(string slug, [FromQuery] string? parentSlug, CancellationToken ct)
    {
        try
        {
            var forum = await _forums.GetBySlugAsync(slug, this.ViewerUserId(), parentSlug, ct);
            return forum is null ? NotFound() : Ok(forum);
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost]
    public async Task<ActionResult<ForumDto>> Create(CreateForumRequest request, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var forumEntity = await _forums.CreateAsync(userId, request, userId, ct);
            await BroadcastForumIfPossible(forumEntity.Slug, userId, ct);
            return CreatedAtAction(nameof(Get), new { slug = forumEntity.Slug }, forumEntity);
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("{slug}/content-blocks")]
    public async Task<ActionResult<IReadOnlyList<ForumContentBlockDto>>> ListBlocks(
        string slug,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var forum = await _forums.GetEntityBySlugAsync(slug, parentSlug, ct);
            if (forum is null) return NotFound();
            return Ok(await _watercoolers.ListForumBlocksAsync(forum.Id, this.ViewerUserId(), ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("{slug}/content-blocks")]
    public async Task<ActionResult<IReadOnlyList<ForumContentBlockDto>>> ReplaceBlocks(
        string slug,
        UpsertForumContentBlocksRequest request,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var forum = await _forums.GetEntityBySlugAsync(slug, parentSlug, ct);
            if (forum is null) return NotFound();
            return Ok(await _watercoolers.ReplaceForumBlocksAsync(forum.Id, userId, request, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("hub-memberships/sync")]
    public async Task<IActionResult> SyncHubMemberships(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _hubFeed.PublishUserMembershipsAsync(userId, ct);
        return Accepted();
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("{slug}/join")]
    public async Task<ActionResult<ForumDto>> Join(string slug, [FromQuery] string? parentSlug, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _forums.JoinAsync(userId, slug, parentSlug, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("{slug}/leave")]
    public async Task<ActionResult<ForumDto>> Leave(string slug, [FromQuery] string? parentSlug, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _forums.LeaveAsync(userId, slug, parentSlug, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("{slug}/favorite")]
    public async Task<ActionResult<ForumDto>> SetFavorite(
        string slug,
        [FromQuery] string? parentSlug,
        [FromBody] SetForumFavoriteRequest request,
        CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _forums.SetFavoriteAsync(userId, slug, request.IsFavorite, parentSlug, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    private async Task BroadcastForumIfPossible(string slug, Guid userId, CancellationToken ct)
    {
        var user = await _auth.GetUserByIdAsync(userId, ct);
        var forum = await _forums.GetEntityBySlugAsync(slug, null, ct);
        if (user is not null && forum is not null)
            await _federation.BroadcastForumAsync(forum, user, ct);
    }
}
