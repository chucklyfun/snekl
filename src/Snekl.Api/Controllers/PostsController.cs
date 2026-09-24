using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/posts")]
public class PostsController : ControllerBase
{
    private readonly IPostService _posts;
    private readonly IPostUpvoteService _upvotes;
    private readonly IPostAggregationService _aggregation;
    private readonly IFederationService _federation;
    private readonly IHubFeedPublisher _hubFeed;
    private readonly IRealtimePublisher _realtime;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly IForumModerationService _moderation;
    private readonly IEmoteService _emotes;

    public PostsController(
        IPostService posts,
        IPostUpvoteService upvotes,
        IPostAggregationService aggregation,
        IFederationService federation,
        IHubFeedPublisher hubFeed,
        IRealtimePublisher realtime,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        IForumModerationService moderation,
        IEmoteService emotes)
    {
        _posts = posts;
        _upvotes = upvotes;
        _aggregation = aggregation;
        _federation = federation;
        _hubFeed = hubFeed;
        _realtime = realtime;
        _environment = environment;
        _configuration = configuration;
        _moderation = moderation;
        _emotes = emotes;
    }

    [HttpGet("recent")]
    public async Task<ActionResult<IReadOnlyList<RecentThreadDto>>> GetRecent(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sort = "hot",
        [FromQuery] string[]? servers = null,
        CancellationToken ct = default)
    {
        try
        {
            var postSort = ParseSort(sort);
            if (servers is { Length: > 0 })
            {
                var authHeader = Request.Headers.Authorization.Count > 0
                    ? Request.Headers.Authorization.ToString()
                    : null;
                return Ok(await _aggregation.GetRecentFromServersAsync(
                    servers, page, pageSize, postSort, authHeader, ct));
            }

            return Ok(await _posts.GetRecentThreadsAsync(page, pageSize, postSort, this.ViewerUserId(), ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [HttpGet("forum/{forumId:guid}")]
    public async Task<ActionResult<IReadOnlyList<PostDto>>> GetForumThreads(
        Guid forumId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sort = "hot",
        CancellationToken ct = default)
    {
        try
        {
            var postSort = ParseSort(sort);
            return Ok(await _posts.GetForumThreadsAsync(forumId, page, pageSize, postSort, this.ViewerUserId(), ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [HttpGet("watercooler/{watercoolerId:guid}")]
    public async Task<ActionResult<IReadOnlyList<PostDto>>> GetWatercoolerStream(
        Guid watercoolerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sort = "new",
        CancellationToken ct = default)
    {
        try
        {
            var postSort = ParseSort(sort);
            return Ok(await _posts.GetWatercoolerStreamAsync(watercoolerId, page, pageSize, postSort, this.ViewerUserId(), ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [HttpGet("{postId:guid}")]
    public async Task<ActionResult<PostDto>> GetThread(Guid postId, CancellationToken ct)
    {
        try
        {
            var thread = await _posts.GetThreadAsync(postId, this.ViewerUserId(), ct);
            return thread is null ? NotFound() : Ok(thread);
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [HttpGet("{postId:guid}/revisions")]
    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    public async Task<ActionResult<IReadOnlyList<PostRevisionDto>>> GetRevisions(Guid postId, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _posts.GetRevisionsAsync(userId, postId, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("{postId:guid}/upvote")]
    public async Task<ActionResult<UpvoteResultDto>> ToggleUpvote(Guid postId, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var thread = await _posts.GetThreadAsync(postId, userId, ct);
            if (thread is null) return NotFound();

            var result = await _upvotes.ToggleAsync(postId, userId, ct);
            var post = await _posts.GetEntityAsync(postId, ct);
            if (post is not null)
            {
                var threadId = await _posts.GetThreadRootIdAsync(postId, ct);
                await _realtime.PublishPostEventAsync(
                    new RealtimePostEvent(
                        "post.upvoted",
                        threadId,
                        post.ForumId,
                        post.Id,
                        post.ParentPostId,
                        UpvoteCount: result.UpvoteCount,
                        WatercoolerId: post.WatercoolerId),
                    ct);
                var root = post.ParentPostId is null ? post : await _posts.GetEntityAsync(threadId, ct);
                if (root is not null)
                    await _hubFeed.PublishThreadScoreAsync(root, ct);
            }

            return Ok(result);
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("{postId:guid}/reactions")]
    public async Task<ActionResult<IReadOnlyList<PostReactionDto>>> ToggleReaction(
        Guid postId,
        ToggleReactionRequest request,
        CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var thread = await _posts.GetThreadAsync(postId, userId, ct);
            if (thread is null) return NotFound();

            var reactions = await _emotes.ToggleReactionAsync(postId, userId, request, ct);
            var post = await _posts.GetEntityAsync(postId, ct);
            if (post is not null)
            {
                var threadId = await _posts.GetThreadRootIdAsync(postId, ct);
                await _realtime.PublishPostEventAsync(
                    new RealtimePostEvent(
                        "post.reacted",
                        threadId,
                        post.ForumId,
                        post.Id,
                        post.ParentPostId,
                        WatercoolerId: post.WatercoolerId),
                    ct);
            }

            return Ok(reactions);
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost]
    public async Task<ActionResult<PostDto>> Create(CreatePostRequest request, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var postDto = await _posts.CreateAsync(userId, request, userId, ct);
            var post = await _posts.GetEntityAsync(postDto.Id, ct);

            if (post?.Forum is not null && post.Author is not null && !post.Author.IsBot)
                await _federation.BroadcastPostAsync(post, post.Forum, post.Author, ct);

            if (post is not null)
            {
                await PushHubAsync(post, deleted: false, ct);
                var threadId = await _posts.GetThreadRootIdAsync(post.Id, ct);
                await _realtime.PublishPostEventAsync(
                    new RealtimePostEvent(
                        "post.created",
                        threadId,
                        post.ForumId,
                        post.Id,
                        post.ParentPostId,
                        WatercoolerId: post.WatercoolerId),
                    ct);
            }

            return CreatedAtAction(nameof(GetThread), new { postId = postDto.Id }, postDto);
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("{postId:guid}")]
    public async Task<ActionResult<PostDto>> Update(Guid postId, UpdatePostRequest request, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var postDto = await _posts.UpdateAsync(userId, postId, request, userId, ct);
            var post = await _posts.GetEntityAsync(postId, ct);
            if (post is not null && post.Forum is not null)
            {
                await _federation.BroadcastPostUpdateAsync(post, ct);
                await PushHubAsync(post, deleted: false, ct);
                var threadId = await _posts.GetThreadRootIdAsync(postId, ct);
                await _realtime.PublishPostEventAsync(
                    new RealtimePostEvent(
                        "post.updated",
                        threadId,
                        post.ForumId,
                        post.Id,
                        post.ParentPostId,
                        IsDeleted: post.IsDeleted),
                    ct);
            }

            return Ok(postDto);
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPatch("{postId:guid}/media/{mediaId:guid}/nsfw")]
    public async Task<ActionResult<PostDto>> SetMediaNsfw(
        Guid postId,
        Guid mediaId,
        UpdatePostMediaNsfwRequest request,
        CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var postDto = await _posts.SetMediaNsfwAsync(userId, postId, mediaId, request, userId, ct);
            var post = await _posts.GetEntityAsync(postId, ct);
            if (post is not null)
            {
                await PushHubAsync(post, deleted: false, ct);
                var threadId = await _posts.GetThreadRootIdAsync(postId, ct);
                await _realtime.PublishPostEventAsync(
                    new RealtimePostEvent(
                        "post.updated",
                        threadId,
                        post.ForumId,
                        post.Id,
                        post.ParentPostId,
                        IsDeleted: post.IsDeleted),
                    ct);
            }

            return Ok(postDto);
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpDelete("{postId:guid}")]
    public async Task<ActionResult<PostDto>> Delete(Guid postId, [FromQuery] bool hard = false, CancellationToken ct = default)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var existing = await _posts.GetEntityAsync(postId, ct);
            if (existing is null) return NotFound();

            var threadId = await _posts.GetThreadRootIdAsync(postId, ct);
            var forumId = existing.ForumId;
            var parentPostId = existing.ParentPostId;

            if (hard)
            {
                if (!_configuration.GetValue("Snekl:AllowHardDelete", _environment.IsDevelopment()))
                    return NotFound();

                await _posts.HardDeleteAsync(userId, postId, ct);
                await PushHubAsync(existing, deleted: true, ct);
                await _realtime.PublishPostEventAsync(
                    new RealtimePostEvent(
                        "post.hardDeleted",
                        threadId,
                        forumId,
                        postId,
                        parentPostId,
                        IsDeleted: true),
                    ct);
                return NoContent();
            }

            var postDto = await _posts.SoftDeleteAsync(userId, postId, userId, ct);
            var post = await _posts.GetEntityAsync(postId, ct);
            if (post is not null && post.Forum is not null)
                await _federation.BroadcastPostDeleteAsync(post, ct);
            await PushHubAsync(post ?? existing, deleted: true, ct);

            await _realtime.PublishPostEventAsync(
                new RealtimePostEvent(
                    "post.deleted",
                    threadId,
                    forumId,
                    postId,
                    parentPostId,
                    IsDeleted: true),
                ct);

            return Ok(postDto);
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("{postId:guid}/lock")]
    public async Task<ActionResult<PostDto>> SetLocked(Guid postId, [FromQuery] bool locked = true, CancellationToken ct = default)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var postDto = await _posts.SetThreadLockedAsync(userId, postId, locked, userId, ct);
            await _realtime.PublishPostEventAsync(
                new RealtimePostEvent(
                    "thread.locked",
                    postDto.Id,
                    postDto.ForumId,
                    postDto.Id,
                    null,
                    IsLocked: postDto.IsLocked),
                ct);
            return Ok(postDto);
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("{postId:guid}/pin")]
    public async Task<ActionResult<PostDto>> SetPinned(Guid postId, [FromQuery] bool pinned = true, CancellationToken ct = default)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var postDto = await _posts.SetThreadPinnedAsync(userId, postId, pinned, userId, ct);
            await _realtime.PublishPostEventAsync(
                new RealtimePostEvent(
                    "thread.pinned",
                    postDto.Id,
                    postDto.ForumId,
                    postDto.Id,
                    null,
                    IsPinned: postDto.IsPinned),
                ct);
            return Ok(postDto);
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("{postId:guid}/reply-policy")]
    public async Task<ActionResult<PostDto>> SetReplyPolicy(
        Guid postId,
        UpdateReplyPolicyRequest request,
        CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var postDto = await _posts.SetReplyPolicyAsync(userId, postId, request.ReplyPolicy, userId, ct);
            await _realtime.PublishPostEventAsync(
                new RealtimePostEvent(
                    "thread.replyPolicy",
                    postDto.Id,
                    postDto.ForumId,
                    postDto.Id,
                    null,
                    WatercoolerId: postDto.WatercoolerId),
                ct);
            return Ok(postDto);
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("{postId:guid}/report")]
    public async Task<ActionResult<PostReportDto>> Report(Guid postId, ReportPostRequest request, CancellationToken ct = default)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _moderation.ReportPostAsync(userId, postId, request.Reason, ct));
        }
        catch (Exception ex) { return ControllerExtensions.HandlePermissionError(ex); }
    }

    private async Task PushHubAsync(Post post, bool deleted, CancellationToken ct)
    {
        if (post.ForumId is null)
            return;
        if (post.ParentPostId is null)
        {
            await _hubFeed.PublishThreadCardAsync(post, deleted, ct);
            return;
        }

        Guid rootId;
        try
        {
            rootId = await _posts.GetThreadRootIdAsync(post.ParentPostId.Value, ct);
        }
        catch (InvalidOperationException)
        {
            rootId = post.ParentPostId.Value;
        }

        var root = await _posts.GetEntityAsync(rootId, ct);
        if (root is not null)
            await _hubFeed.PublishThreadScoreAsync(root, ct);
    }

    private static PostSort ParseSort(string sort)
    {
        if (sort.Equals("new", StringComparison.OrdinalIgnoreCase)) return PostSort.New;
        if (sort.Equals("updated", StringComparison.OrdinalIgnoreCase)) return PostSort.Updated;
        return PostSort.Hot;
    }
}
