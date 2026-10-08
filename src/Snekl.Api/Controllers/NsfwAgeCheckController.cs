using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/me/nsfw-age-check")]
public class NsfwAgeCheckController : ControllerBase
{
    private readonly INsfwAgeCheckService _ageCheck;

    public NsfwAgeCheckController(INsfwAgeCheckService ageCheck) => _ageCheck = ageCheck;

    [HttpGet]
    public async Task<ActionResult<NsfwAgeCheckStatusDto>> Get([FromQuery] Guid? forumId, CancellationToken ct)
    {
        var status = await _ageCheck.GetStatusAsync(this.ViewerUserId(), forumId, ct);
        return Ok(status);
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost]
    public async Task<ActionResult<NsfwAgeCheckStatusDto>> Submit(SubmitNsfwAgeCheckRequest request, CancellationToken ct)
    {
        try
        {
            var status = await _ageCheck.SubmitAsync(this.RequireUserId(), request.DateOfBirth, request.ForumId, ct);
            return Ok(status);
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }
}
