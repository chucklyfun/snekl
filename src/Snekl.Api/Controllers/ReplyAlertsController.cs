using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/reply-alerts")]
[Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
public class ReplyAlertsController : ControllerBase
{
    private readonly IReplyAlertService _alerts;

    public ReplyAlertsController(IReplyAlertService alerts) => _alerts = alerts;

    private async Task<ActionResult<PlaceAlertPreferenceDto>> GetPlace(string kind, Guid placeId, CancellationToken ct)
    {
        try
        {
            return Ok(await _alerts.GetPlacePreferenceAsync(this.RequireUserId(), kind, placeId, ct));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    private async Task<ActionResult<PlaceAlertPreferenceDto>> SetPlace(
        string kind,
        Guid placeId,
        string mode,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _alerts.SetPlacePreferenceAsync(this.RequireUserId(), kind, placeId, mode, ct));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReplyAlertDto>>> List(
        [FromQuery] int limit = 50,
        CancellationToken ct = default) =>
        Ok(await _alerts.ListAsync(this.RequireUserId(), limit, ct));

    [HttpGet("unread-count")]
    public async Task<ActionResult<ReplyAlertUnreadCountDto>> UnreadCount(CancellationToken ct) =>
        Ok(new ReplyAlertUnreadCountDto(await _alerts.UnreadCountAsync(this.RequireUserId(), ct)));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        try
        {
            await _alerts.MarkReadAsync(this.RequireUserId(), id, ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [HttpGet("threads/{postId:guid}")]
    public async Task<ActionResult<ReplyAlertPreferenceDto>> GetPreference(Guid postId, CancellationToken ct)
    {
        try
        {
            return Ok(await _alerts.GetPreferenceAsync(this.RequireUserId(), postId, ct));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [HttpGet("nests/{forumId:guid}")]
    public Task<ActionResult<PlaceAlertPreferenceDto>> GetNestPreference(Guid forumId, CancellationToken ct) =>
        GetPlace(ReplyAlertKinds.Nest, forumId, ct);

    [HttpPut("nests/{forumId:guid}")]
    public Task<ActionResult<PlaceAlertPreferenceDto>> SetNestPreference(
        Guid forumId,
        SetPlaceAlertPreferenceRequest request,
        CancellationToken ct) =>
        SetPlace(ReplyAlertKinds.Nest, forumId, request.Mode, ct);

    [HttpGet("watercoolers/{watercoolerId:guid}")]
    public Task<ActionResult<PlaceAlertPreferenceDto>> GetWatercoolerPreference(Guid watercoolerId, CancellationToken ct) =>
        GetPlace(ReplyAlertKinds.Watercooler, watercoolerId, ct);

    [HttpPut("watercoolers/{watercoolerId:guid}")]
    public Task<ActionResult<PlaceAlertPreferenceDto>> SetWatercoolerPreference(
        Guid watercoolerId,
        SetPlaceAlertPreferenceRequest request,
        CancellationToken ct) =>
        SetPlace(ReplyAlertKinds.Watercooler, watercoolerId, request.Mode, ct);

    [HttpPut("threads/{postId:guid}")]
    public async Task<ActionResult<ReplyAlertPreferenceDto>> SetPreference(
        Guid postId,
        SetReplyAlertPreferenceRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _alerts.SetPreferenceAsync(this.RequireUserId(), postId, request.Mode, ct));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [HttpPost("devices")]
    public async Task<IActionResult> RegisterDevice(RegisterPushDeviceRequest request, CancellationToken ct)
    {
        try
        {
            await _alerts.RegisterDeviceAsync(this.RequireUserId(), request.Platform, request.Token, ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [HttpDelete("devices")]
    public async Task<IActionResult> UnregisterDevice(RemovePushDeviceRequest request, CancellationToken ct)
    {
        await _alerts.UnregisterDeviceAsync(this.RequireUserId(), request.Token, ct);
        return NoContent();
    }
}
