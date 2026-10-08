using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/forums")]
public class ForumsController : ControllerBase
{
    private readonly IForumService _forums;
    private readonly INestMapService _nestMap;
    private readonly IWatercoolerService _watercoolers;
    private readonly IForumPageService _forumPages;
    private readonly IFederationService _federation;
    private readonly IAuthService _auth;
    private readonly IHubFeedPublisher _hubFeed;

    public ForumsController(
        IForumService forums,
        INestMapService nestMap,
        IWatercoolerService watercoolers,
        IForumPageService forumPages,
        IFederationService federation,
        IAuthService auth,
        IHubFeedPublisher hubFeed)
    {
        _forums = forums;
        _nestMap = nestMap;
        _watercoolers = watercoolers;
        _forumPages = forumPages;
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
    public async Task<IActionResult> Get(string slug, [FromQuery] string? parentSlug, CancellationToken ct)
    {
        try
        {
            var viewerId = this.ViewerUserId();
            var existing = await _forums.GetEntityBySlugAsync(slug, parentSlug, ct);
            if (existing is not null)
            {
                var forum = await _forums.GetBySlugAsync(slug, viewerId, parentSlug, ct);
                return forum is null ? NotFound() : Ok(forum);
            }

            var redirect = await _nestMap.RedirectForAsync(slug, parentSlug, viewerId, ct);
            return redirect is null ? NotFound() : Ok(new { redirect });
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
            if (forumEntity.ApprovalStatus == ForumApprovalStatuses.Approved)
                await BroadcastForumIfPossible(forumEntity.Slug, userId, ct);
            return CreatedAtAction(nameof(Get), new { slug = forumEntity.Slug }, forumEntity);
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("{slug}/approve")]
    public async Task<ActionResult<ForumDto>> Approve(string slug, [FromQuery] string? parentSlug, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var existing = await _forums.GetEntityBySlugAsync(slug, parentSlug, ct);
            if (existing is null) return NotFound();
            var forum = await _forums.ApproveAsync(userId, existing.Id, ct);
            var author = await _auth.GetUserByIdAsync(existing.CreatedByUserId, ct);
            if (author is not null && !author.IsBot)
                await _federation.BroadcastForumAsync(existing, author, ct);
            return Ok(forum);
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("{slug}/reject")]
    public async Task<IActionResult> Reject(string slug, [FromQuery] string? parentSlug, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var existing = await _forums.GetEntityBySlugAsync(slug, parentSlug, ct);
            if (existing is null) return NotFound();
            await _forums.RejectAsync(userId, existing.Id, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpDelete("{slug}")]
    public async Task<IActionResult> Delete(string slug, [FromQuery] string? parentSlug, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var existing = await _forums.GetEntityBySlugAsync(slug, parentSlug, ct);
            if (existing is null) return NotFound();
            await _forums.DeleteAsync(userId, existing.Id, ct);
            return NoContent();
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
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

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("{slug}/pages/home")]
    public async Task<ActionResult<ForumPageDto>> GetHomePage(
        string slug,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var forum = await _forums.GetEntityBySlugAsync(slug, parentSlug, ct);
            if (forum is null) return NotFound();
            return Ok(await _forumPages.GetDefaultHomeAsync(forum.Id, this.ViewerUserId(), ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("{slug}/pages/home")]
    public async Task<ActionResult<ForumPageDto>> UpdateHomePageLayout(
        string slug,
        UpdateForumHomeLayoutRequest request,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var forum = await _forums.GetEntityBySlugAsync(slug, parentSlug, ct);
            if (forum is null) return NotFound();
            return Ok(await _forumPages.UpdateDefaultHomeLayoutAsync(forum.Id, userId, request, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("{slug}/pages/chrome")]
    public async Task<ActionResult<ForumPageDto>> GetChromePage(
        string slug,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var forum = await _forums.GetEntityBySlugAsync(slug, parentSlug, ct);
            if (forum is null) return NotFound();
            return Ok(await _forumPages.GetChromeAsync(forum.Id, this.ViewerUserId(), ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("{slug}/pages/chrome")]
    public async Task<ActionResult<ForumPageDto>> UpdateChromePageLayout(
        string slug,
        UpdateForumChromeLayoutRequest request,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var forum = await _forums.GetEntityBySlugAsync(slug, parentSlug, ct);
            if (forum is null) return NotFound();
            return Ok(await _forumPages.UpdateChromeLayoutAsync(forum.Id, userId, request, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("{slug}/pages")]
    public async Task<ActionResult<IReadOnlyList<ForumPageSummaryDto>>> ListPages(
        string slug,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var forum = await _forums.GetEntityBySlugAsync(slug, parentSlug, ct);
            if (forum is null) return NotFound();
            return Ok(await _forumPages.ListPagesAsync(forum.Id, this.ViewerUserId(), ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("{slug}/pages")]
    public async Task<ActionResult<ForumPageDto>> CreatePage(
        string slug,
        CreateForumPageRequest request,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var forum = await _forums.GetEntityBySlugAsync(slug, parentSlug, ct);
            if (forum is null) return NotFound();
            return Ok(await _forumPages.CreatePageAsync(forum.Id, userId, request, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("{slug}/pages/{pageSlug}")]
    public async Task<ActionResult<ForumPageDto>> GetPage(
        string slug,
        string pageSlug,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var forum = await _forums.GetEntityBySlugAsync(slug, parentSlug, ct);
            if (forum is null) return NotFound();
            return Ok(await _forumPages.GetPageAsync(forum.Id, pageSlug, this.ViewerUserId(), ct));
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("{slug}/pages/{pageSlug}")]
    public async Task<ActionResult<ForumPageDto>> UpdatePageLayout(
        string slug,
        string pageSlug,
        UpdateForumPageLayoutRequest request,
        [FromQuery] string? parentSlug,
        CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var forum = await _forums.GetEntityBySlugAsync(slug, parentSlug, ct);
            if (forum is null) return NotFound();
            return Ok(await _forumPages.UpdatePageLayoutAsync(forum.Id, pageSlug, userId, request, ct));
        }
        catch (KeyNotFoundException) { return NotFound(); }
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

    private async Task BroadcastForumIfPossible(string slug, Guid userId, CancellationToken ct)
    {
        var user = await _auth.GetUserByIdAsync(userId, ct);
        var forum = await _forums.GetEntityBySlugAsync(slug, null, ct);
        if (user is not null && !user.IsBot && forum is not null)
            await _federation.BroadcastForumAsync(forum, user, ct);
    }
}
