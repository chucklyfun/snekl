using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/posts")]
public class PostsController : ControllerBase
{
    private readonly IPostService _posts;
    private readonly IFederationService _federation;

    public PostsController(IPostService posts, IFederationService federation)
    {
        _posts = posts;
        _federation = federation;
    }

    private Guid? ViewerId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return claim is null ? null : Guid.Parse(claim);
    }

    [HttpGet("forum/{forumId:guid}")]
    public async Task<ActionResult<IReadOnlyList<PostDto>>> GetForumThreads(
        Guid forumId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        Ok(await _posts.GetForumThreadsAsync(forumId, page, pageSize, ViewerId(), ct));

    [HttpGet("{postId:guid}")]
    public async Task<ActionResult<PostDto>> GetThread(Guid postId, CancellationToken ct)
    {
        var thread = await _posts.GetThreadAsync(postId, ViewerId(), ct);
        return thread is null ? NotFound() : Ok(thread);
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<PostDto>> Create(CreatePostRequest request, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var postDto = await _posts.CreateAsync(userId, request, userId, ct);
            var post = await _posts.GetEntityAsync(postDto.Id, ct);

            if (post?.Forum is not null && post.Author is not null)
                await _federation.BroadcastPostAsync(post, post.Forum, post.Author, ct);

            return CreatedAtAction(nameof(GetThread), new { postId = postDto.Id }, postDto);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
