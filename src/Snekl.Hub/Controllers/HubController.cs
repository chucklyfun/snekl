using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Hub.Domain.Dtos;
using Snekl.Hub.Infrastructure.Security;
using Snekl.Hub.Infrastructure.Services;

namespace Snekl.Hub.Controllers;

[ApiController]
[Route("api/hub")]
public class HubController : ControllerBase
{
    private readonly IFederationRegistry _registry;
    private readonly IHubFeedService _feed;
    private readonly IOriginJoinClient _originJoins;
    private readonly IHubIngestService _ingest;
    private readonly IConfiguration _config;

    public HubController(
        IFederationRegistry registry,
        IHubFeedService feed,
        IHubIngestService ingest,
        IOriginJoinClient originJoins,
        IConfiguration config)
    {
        _registry = registry;
        _feed = feed;
        _ingest = ingest;
        _originJoins = originJoins;
        _config = config;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<HubInfoDto>> GetInfo(CancellationToken ct)
    {
        var mapVersion = await _registry.GetMapVersionAsync(ct);
        var name = _config["Hub:Name"] ?? "Snekl Hub";
        var baseUrl = (_config["Hub:BaseUrl"] ?? "http://localhost:5200").TrimEnd('/');
        var authBaseUrl = (_config["Hub:AuthBaseUrl"] ?? "http://localhost:5100").TrimEnd('/');
        return Ok(new HubInfoDto(name, baseUrl, authBaseUrl, mapVersion));
    }

    [HttpGet("config")]
    [AllowAnonymous]
    public Task<ActionResult<FederationConfigDto>> GetConfig(CancellationToken ct) => BuildConfigAsync(ct);

    [HttpGet("map")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<FederationMapDto>> GetMap(CancellationToken ct) =>
        Ok(await _registry.GetMapAsync(ct));

    [HttpPost("map/ack")]
    [AllowAnonymous]
    public async Task<IActionResult> AckMap([FromBody] MapAckRequest request, CancellationToken ct)
    {
        try
        {
            await _registry.AckMapAsync(request, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("servers")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<RegisteredServerDto>>> ListServers(CancellationToken ct) =>
        Ok(await _registry.ListServersAsync(ct));

    [HttpGet("join-requests")]
    [Authorize(AuthenticationSchemes = HubAdminAuth.Scheme, Policy = HubAdminAuth.Policy)]
    public async Task<ActionResult<IReadOnlyList<RegisteredServerDto>>> ListJoinRequests(
        [FromQuery] string? serverDid,
        CancellationToken ct) =>
        Ok(await _registry.ListJoinRequestsAsync(serverDid, ct));

    [HttpPost("servers")]
    [Authorize(AuthenticationSchemes = HubAdminAuth.Scheme, Policy = HubAdminAuth.Policy)]
    public async Task<ActionResult<RegisteredServerDto>> RegisterServer(
        [FromBody] RegisterServerRequest request,
        CancellationToken ct)
    {
        var memberAuthorization = Request.Headers[HubAdminAuth.MemberAuthorizationHeader].ToString();
        if (string.IsNullOrWhiteSpace(memberAuthorization))
            return BadRequest(new { error = "Member session is required to send an invite." });

        try
        {
            var hubName = _config["Hub:Name"] ?? "Snekl Hub";
            var hubBaseUrl = (_config["Hub:BaseUrl"] ?? "http://localhost:5200").TrimEnd('/');
            var inviteId = await _originJoins.RequestInviteAsync(
                request.BaseUrl, memberAuthorization, hubBaseUrl, hubName, ct);
            var server = await _registry.RegisterServerAsync(
                request with { InitiatedBy = "hub", OriginInviteId = inviteId }, ct);
            return Ok(server);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            var status = ex.Message.Contains("RequestFederation", StringComparison.Ordinal)
                ? HttpStatusCode.Forbidden
                : HttpStatusCode.BadRequest;
            return StatusCode((int)status, new { error = ex.Message });
        }
    }

    [HttpPost("join-requests/accept")]
    [AllowAnonymous]
    public async Task<ActionResult<RegisteredServerDto>> AcceptJoin(
        [FromBody] AcceptJoinRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _registry.AcceptJoinAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("join-requests/decline")]
    [AllowAnonymous]
    public async Task<IActionResult> DeclineJoin(
        [FromBody] DeclineJoinRequest request,
        CancellationToken ct)
    {
        try
        {
            await _registry.DeclineJoinAsync(request, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("servers")]
    [HttpDelete("servers/{*did}")]
    [AllowAnonymous]
    public async Task<IActionResult> UnregisterServer(
        [FromQuery] string? serverDid,
        [FromRoute] string? did,
        CancellationToken ct)
    {
        var key = !string.IsNullOrWhiteSpace(serverDid) ? serverDid : did;
        if (string.IsNullOrWhiteSpace(key))
            return BadRequest(new { error = "ServerDid is required." });

        try
        {
            await _registry.UnregisterServerAsync(key, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("feed")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<HubFeedCardDto>>> GetFeed(
        [FromQuery] string sort = "hot",
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var viewerDid = User.FindFirstValue("did");
        return Ok(await _feed.GetFeedAsync(sort, limit, viewerDid, offset, ct));
    }

    [HttpGet("search")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<HubSearchHitDto>>> Search(
        [FromQuery] string? q,
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var viewerDid = User.FindFirstValue("did");
        return Ok(await _feed.SearchAsync(q, limit, viewerDid, offset, ct));
    }

    /// <summary>Signed federation envelope when Hub:RequireSignatures is true. Active registry membership is required.</summary>
    [HttpPost("cards")]
    [AllowAnonymous]
    public async Task<IActionResult> IngestCard([FromBody] JsonElement body, CancellationToken ct)
    {
        try
        {
            await _ingest.AcceptCardAsync(body, ct);
            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Signed score patches. Unknown posts are ignored.</summary>
    [HttpPost("scores")]
    [AllowAnonymous]
    public async Task<IActionResult> IngestScores([FromBody] JsonElement body, CancellationToken ct)
    {
        try
        {
            await _ingest.AcceptScoresAsync(body, ct);
            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Origin-pushed forum membership index. Active registry membership is required.</summary>
    [HttpPost("memberships")]
    [AllowAnonymous]
    public async Task<IActionResult> IngestMembership([FromBody] JsonElement body, CancellationToken ct)
    {
        try
        {
            await _ingest.AcceptMembershipAsync(body, ct);
            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("memberships/sync")]
    [AllowAnonymous]
    public async Task<IActionResult> SyncMemberships([FromBody] JsonElement body, CancellationToken ct)
    {
        try
        {
            await _ingest.AcceptMembershipSyncAsync(body, ct);
            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    internal async Task<ActionResult<FederationConfigDto>> BuildConfigAsync(CancellationToken ct)
    {
        var map = await _registry.GetMapAsync(ct);
        var name = _config["Hub:Name"] ?? "Snekl Hub";
        var baseUrl = (_config["Hub:BaseUrl"] ?? "http://localhost:5200").TrimEnd('/');
        var authBaseUrl = (_config["Hub:AuthBaseUrl"] ?? "http://localhost:5100").TrimEnd('/');
        var servers = map.Servers
            .Select(s => new FederationConfigServerDto(s.ServerDid, s.BaseUrl, s.Name))
            .ToList();
        return Ok(new FederationConfigDto(
            FederationConfigSchema.V1,
            name,
            baseUrl,
            new FederationAuthConfigDto(authBaseUrl, $"{authBaseUrl}/.well-known/jwks.json"),
            map.MapVersion,
            servers,
            $"{baseUrl}/api/hub/feed"));
    }
}
