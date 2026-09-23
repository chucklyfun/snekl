using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Hub.Domain.Dtos;
using Snekl.Hub.Infrastructure.Services;

namespace Snekl.Hub.Controllers;

[ApiController]
public class FederationWellKnownController : ControllerBase
{
    private readonly IFederationRegistry _registry;
    private readonly IConfiguration _config;

    public FederationWellKnownController(IFederationRegistry registry, IConfiguration config)
    {
        _registry = registry;
        _config = config;
    }

    [HttpGet(".well-known/snekl-federation.json")]
    [AllowAnonymous]
    public async Task<ActionResult<FederationConfigDto>> Get(CancellationToken ct)
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

[ApiController]
[Route("admin")]
public class HubAdminBootstrapController : ControllerBase
{
    private readonly IConfiguration _config;

    public HubAdminBootstrapController(IConfiguration config) => _config = config;

    [HttpGet("bootstrap")]
    [AllowAnonymous]
    public ActionResult<AdminBootstrapDto> Bootstrap()
    {
        var name = _config["Hub:Name"] ?? "Snekl Hub";
        var baseUrl = (_config["Hub:BaseUrl"] ?? "http://localhost:5200").TrimEnd('/');
        var authBaseUrl = (_config["Hub:AuthBaseUrl"] ?? "http://localhost:5100").TrimEnd('/');
        return Ok(new AdminBootstrapDto("hub", name, baseUrl, null, authBaseUrl));
    }
}
