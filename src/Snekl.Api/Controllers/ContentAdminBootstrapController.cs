using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("server")]
public class ContentAdminBootstrapController : ControllerBase
{
    private readonly INodeService _node;
    private readonly IPersonClusterMappingService _identity;

    public ContentAdminBootstrapController(INodeService node, IPersonClusterMappingService identity)
    {
        _node = node;
        _identity = identity;
    }

    [HttpGet("bootstrap")]
    [AllowAnonymous]
    public async Task<ActionResult<AdminBootstrapDto>> Bootstrap(CancellationToken ct)
    {
        var node = await _node.GetOrCreateAsync(ct);
        var issuers = await _identity.ListTrustedIssuersAsync(ct);
        var authLoginUrl = issuers.Count > 0
            ? issuers[0].IssuerUrl.TrimEnd('/')
            : "http://localhost:5100";
        return Ok(new AdminBootstrapDto("content", node.Name, node.BaseUrl, node.Did, authLoginUrl));
    }
}
