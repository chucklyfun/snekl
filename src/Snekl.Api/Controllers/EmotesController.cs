using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/emotes")]
public class EmotesController : ControllerBase
{
    private readonly IEmoteService _emotes;

    public EmotesController(IEmoteService emotes) => _emotes = emotes;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EmoteDto>>> List(
        [FromQuery] Guid? forumId,
        [FromQuery] bool allViewable,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _emotes.ListAvailableAsync(forumId, this.ViewerUserId(), allViewable, ct));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost]
    public async Task<ActionResult<EmoteDto>> Create(CreateEmoteRequest request, CancellationToken ct)
    {
        try
        {
            var created = await _emotes.CreateAsync(this.RequireUserId(), request, ct);
            return CreatedAtAction(nameof(List), new { forumId = created.ForumId }, created);
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("order")]
    public async Task<IActionResult> Reorder([FromBody] ReorderEmotesRequest request, CancellationToken ct)
    {
        try
        {
            await _emotes.ReorderAsync(this.RequireUserId(), request, ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("{emoteId:guid}")]
    public async Task<ActionResult<EmoteDto>> Update(Guid emoteId, [FromBody] UpdateEmoteRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _emotes.UpdateAsync(this.RequireUserId(), emoteId, request, ct));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpDelete("{emoteId:guid}")]
    public async Task<IActionResult> Delete(Guid emoteId, CancellationToken ct)
    {
        try
        {
            await _emotes.DeleteAsync(this.RequireUserId(), emoteId, ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }
}
