using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/network")]
public class NetworkController : ControllerBase
{
    private readonly IFederationService _federation;

    public NetworkController(IFederationService federation) => _federation = federation;

    /// <summary>
    /// Servers in this node's network (self plus federation peers).
    /// Apps aggregate feeds by fetching owned posts from each member — no deduplication needed.
    /// </summary>
    [HttpGet("servers")]
    public async Task<ActionResult<IReadOnlyList<NetworkServerDto>>> ListServers(CancellationToken ct) =>
        Ok(await _federation.ListNetworkServersAsync(ct));
}
