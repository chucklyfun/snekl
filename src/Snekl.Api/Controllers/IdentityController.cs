using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Identity;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/identity")]
public class IdentityController : ControllerBase
{
    private readonly IPersonClusterMappingService _mapping;

    public IdentityController(IPersonClusterMappingService mapping) => _mapping = mapping;

    [HttpGet("clusters/{clusterId:guid}")]
    public async Task<ActionResult<PersonClusterRefDto>> GetCluster(Guid clusterId, CancellationToken ct)
    {
        var cluster = await _mapping.GetClusterAsync(clusterId, ct);
        return cluster is null ? NotFound() : Ok(cluster);
    }

    [HttpPost("map")]
    public async Task<ActionResult<PersonClusterRefDto>> MapIdentity(MapIdentityRequest request, CancellationToken ct) =>
        Ok(await _mapping.MapIdentityAsync(request, ct));

    [HttpGet("issuers")]
    public async Task<ActionResult<IReadOnlyList<TrustedAuthIssuer>>> ListIssuers(CancellationToken ct) =>
        Ok(await _mapping.ListTrustedIssuersAsync(ct));

    [HttpPost("issuers")]
    public async Task<ActionResult<TrustedAuthIssuer>> RegisterIssuer(RegisterTrustedIssuerRequest request, CancellationToken ct) =>
        Ok(await _mapping.RegisterTrustedIssuerAsync(request, ct));
}
