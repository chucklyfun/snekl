using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/admin/host")]
[Authorize(Policy = SiteAdminAuthorization.SiteAdminPolicy)]
public class HostServersController : ControllerBase
{
    private readonly IVirtualServerService _servers;
    private readonly INestTransferService _nests;

    public HostServersController(IVirtualServerService servers, INestTransferService nests)
    {
        _servers = servers;
        _nests = nests;
    }

    [HttpGet("servers")]
    public async Task<ActionResult<IReadOnlyList<VirtualServerSummary>>> List(CancellationToken ct) =>
        Ok(await _servers.ListAsync(ct));

    [HttpPost("servers")]
    public async Task<ActionResult<VirtualServerSummary>> Spawn(SpawnServerRequest request, CancellationToken ct)
    {
        try
        {
            var created = await _servers.SpawnAsync(request, ct);
            return Ok(created);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("servers/{serverId:guid}/disable")]
    public async Task<IActionResult> Disable(Guid serverId, CancellationToken ct)
    {
        try
        {
            await _servers.DisableAsync(serverId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("servers/{serverId:guid}/enable")]
    public async Task<IActionResult> Enable(Guid serverId, CancellationToken ct)
    {
        try
        {
            await _servers.EnableAsync(serverId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("servers/{serverId:guid}/purge")]
    public async Task<IActionResult> Purge(Guid serverId, CancellationToken ct)
    {
        try
        {
            await _servers.PurgeAsync(serverId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("servers/{serverId:guid}/nests")]
    public async Task<ActionResult<IReadOnlyList<NestMapNodeDto>>> Nests(Guid serverId, CancellationToken ct)
    {
        try
        {
            return Ok(await _nests.ListAsync(serverId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("servers/{serverId:guid}/nests/{forumId:guid}/move")]
    public async Task<IActionResult> MoveNest(Guid serverId, Guid forumId, MoveNestRequest request, CancellationToken ct)
    {
        try
        {
            await _nests.MoveAsync(serverId, forumId, request.DestinationServerId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("template")]
    public ContentResult GetTemplate() => TemplateJson(_servers.GetTemplate());

    [HttpPut("template")]
    public ActionResult SaveTemplate([FromBody] JsonElement body)
    {
        try
        {
            var document = body.Deserialize<ServerTemplateDocument>(ServerTemplateStore.JsonOptions)
                ?? new ServerTemplateDocument();
            _servers.SaveTemplate(document);
            return TemplateJson(_servers.GetTemplate());
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static ContentResult TemplateJson(ServerTemplateDocument document) =>
        new()
        {
            Content = JsonSerializer.Serialize(document, ServerTemplateStore.JsonOptions),
            ContentType = "application/json"
        };
}
