using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/dm")]
[Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
public class DirectMessagesController : ControllerBase
{
    private readonly IDirectConversationService _dms;

    public DirectMessagesController(IDirectConversationService dms) => _dms = dms;

    [HttpGet("eligibility/{username}")]
    public async Task<ActionResult<DirectMessageEligibilityDto>> Eligibility(string username, CancellationToken ct)
    {
        try
        {
            return Ok(await _dms.GetEligibilityAsync(this.RequireUserId(), username, ct));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [HttpGet]
    public async Task<ActionResult<DirectMessageInboxDto>> Inbox(CancellationToken ct)
    {
        return Ok(await _dms.GetInboxAsync(this.RequireUserId(), ct));
    }

    [HttpPost("requests")]
    public async Task<ActionResult<DirectConversationRequestDto>> CreateRequest(
        CreateDirectConversationRequestDto request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _dms.CreateRequestAsync(this.RequireUserId(), request, ct));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [HttpPost("requests/{requestId:guid}/accept")]
    public async Task<ActionResult<DirectConversationRequestDto>> Accept(Guid requestId, CancellationToken ct)
    {
        try
        {
            return Ok(await _dms.AcceptAsync(this.RequireUserId(), requestId, ct));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [HttpPost("requests/{requestId:guid}/decline")]
    public async Task<ActionResult<DirectConversationRequestDto>> Decline(Guid requestId, CancellationToken ct)
    {
        try
        {
            return Ok(await _dms.DeclineAsync(this.RequireUserId(), requestId, ct));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [HttpPost("requests/{requestId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid requestId, CancellationToken ct)
    {
        try
        {
            await _dms.CancelAsync(this.RequireUserId(), requestId, ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [HttpGet("conversations/{conversationId:guid}")]
    public async Task<ActionResult<DirectConversationDto>> GetConversation(Guid conversationId, CancellationToken ct)
    {
        try
        {
            return Ok(await _dms.GetConversationAsync(this.RequireUserId(), conversationId, ct));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [HttpPost("conversations/{conversationId:guid}/messages")]
    public async Task<ActionResult<DirectMessageDto>> SendMessage(
        Guid conversationId,
        SendDirectMessageRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _dms.SendMessageAsync(this.RequireUserId(), conversationId, request.Body, ct));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [HttpGet("forum-preferences")]
    public async Task<ActionResult<DirectMessageForumPreferencesDto>> ListForumPreferences(CancellationToken ct)
    {
        try
        {
            return Ok(await _dms.ListForumPreferencesAsync(this.RequireUserId(), ct));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [HttpPut("forum-preferences/{forumId:guid}")]
    public async Task<ActionResult<DirectMessageForumPreferenceDto>> SetForumPreference(
        Guid forumId,
        SetDirectMessageForumPreferenceRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _dms.SetForumPreferenceAsync(this.RequireUserId(), forumId, request.Mode, ct));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }
}
