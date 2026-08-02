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
    private readonly IFederationService _federation;
    private readonly IAuthService _auth;

    public ForumsController(IForumService forums, IFederationService federation, IAuthService auth)
    {
        _forums = forums;
        _federation = federation;
        _auth = auth;
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

    private async Task BroadcastForumIfPossible(string slug, Guid userId, CancellationToken ct)
    {
        var user = await _auth.GetUserByIdAsync(userId, ct);
        var forum = await _forums.GetEntityBySlugAsync(slug, null, ct);
        if (user is not null && forum is not null)
            await _federation.BroadcastForumAsync(forum, user, ct);
    }
}
