using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/media")]
public class MediaController : ControllerBase
{
    private readonly IMediaService _media;

    public MediaController(IMediaService media) => _media = media;

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 10 * 1024 * 1024)]
    public async Task<ActionResult<MediaAttachmentDto>> Upload(IFormFile file, CancellationToken ct)
    {
        try
        {
            if (file is null || file.Length == 0)
                return BadRequest(new { error = "A non-empty image file is required." });

            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await using var stream = file.OpenReadStream();
            var dto = await _media.UploadAsync(userId, stream, file.ContentType, file.FileName, file.Length, ct);
            return Ok(dto);
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }

    [HttpGet("{mediaId:guid}")]
    public async Task<IActionResult> Get(Guid mediaId, CancellationToken ct)
    {
        var opened = await _media.OpenOriginalAsync(mediaId, this.ViewerUserId(), ct);
        if (opened is null) return NotFound();

        Response.Headers.CacheControl = opened.Value.CachePublic
            ? "public, max-age=3600"
            : "private, no-store";
        return File(opened.Value.Stream, opened.Value.ContentType);
    }

    [HttpGet("{mediaId:guid}/preview")]
    public async Task<IActionResult> Preview(Guid mediaId, CancellationToken ct)
    {
        var opened = await _media.OpenPreviewAsync(mediaId, ct);
        if (opened is null) return NotFound();

        Response.Headers.CacheControl = "public, max-age=3600";
        return File(opened.Value.Stream, opened.Value.ContentType);
    }
}
