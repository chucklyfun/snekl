using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/extensions")]
public class ExtensionsController : ControllerBase
{
    private readonly IExtensionInstallService _installs;

    public ExtensionsController(IExtensionInstallService installs) => _installs = installs;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExtensionInstallDto>>> List(CancellationToken ct) =>
        Ok(await _installs.ListActiveAsync(ct));

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("install-options")]
    public async Task<ActionResult<ExtensionInstallOptionsDto>> Options(
        [FromQuery] string? clientId,
        CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(await _installs.GetOptionsAsync(userId, clientId, ct));
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("installs/{installId:guid}")]
    public async Task<ActionResult<ExtensionInstallDto>> Get(Guid installId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var install = await _installs.GetAsync(userId, installId, ct);
        return install is null ? NotFound(new { error = "Install not found." }) : Ok(install);
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("installs")]
    public async Task<ActionResult<ExtensionInstallDto>> Create(CreateExtensionInstallRequest request, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var created = await _installs.CreateAsync(userId, request, ct);
            return CreatedAtAction(nameof(Get), new { installId = created.Id }, created);
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }
}
