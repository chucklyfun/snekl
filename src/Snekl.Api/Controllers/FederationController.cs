using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Domain.Identity.Did;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/node")]
public class NodeController : ControllerBase
{
    private readonly INodeService _node;
    private readonly IPersonClusterMappingService _identity;

    public NodeController(INodeService node, IPersonClusterMappingService identity)
    {
        _node = node;
        _identity = identity;
    }

    [HttpGet]
    public async Task<ActionResult<NodeInfoDto>> Get(CancellationToken ct) =>
        Ok(await _node.GetInfoAsync(ct));

    [HttpGet(".well-known/snekl")]
    public Task<ActionResult<NodeInfoDto>> WellKnown(CancellationToken ct) => Get(ct);

    /// <summary>Auth issuers this origin trusts. Independent of hub membership.</summary>
    [HttpGet("auth")]
    public async Task<ActionResult<NodeAuthInfoDto>> Auth(CancellationToken ct)
    {
        var issuers = await _identity.ListTrustedIssuersAsync(ct);
        return Ok(new NodeAuthInfoDto(
            issuers.Select(i => new TrustedAuthIssuerDto(i.Name, i.IssuerUrl, i.JwksUrl)).ToList()));
    }
}

[ApiController]
public class ContentDidController : ControllerBase
{
    private readonly INodeService _node;

    public ContentDidController(INodeService node) => _node = node;

    [HttpGet(".well-known/did.json")]
    public async Task<ActionResult<DidDocument>> DidDocument(CancellationToken ct)
    {
        var node = await _node.GetOrCreateAsync(ct);
        var vmId = _node.GetVerificationMethodId(node);
        return Ok(new DidDocument(
            [
                "https://www.w3.org/ns/did/v1",
                "https://w3id.org/security/suites/ed25519-2020/v1"
            ],
            node.Did,
            [new DidVerificationMethod(vmId, "Ed25519VerificationKey2020", node.Did, node.VerificationKeyMultibase)],
            [vmId],
            [vmId]));
    }
}

[ApiController]
[Route("api/federation")]
public class FederationController : ControllerBase
{
    private readonly IFederationService _federation;

    public FederationController(IFederationService federation) => _federation = federation;

    [HttpGet("peers")]
    public async Task<ActionResult<IReadOnlyList<PeerDto>>> ListPeers(CancellationToken ct) =>
        Ok(await _federation.ListPeersAsync(ct));

    [Authorize(Policy = SiteAdminAuthorization.SiteAdminPolicy)]
    [HttpPost("peers")]
    public async Task<ActionResult<PeerDto>> Subscribe(SubscribePeerRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _federation.SubscribeAsync(request.BaseUrl, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Policy = SiteAdminAuthorization.SiteAdminPolicy)]
    [HttpDelete("peers/{peerId:guid}")]
    public async Task<IActionResult> Unsubscribe(Guid peerId, CancellationToken ct)
    {
        try
        {
            await _federation.UnsubscribeAsync(peerId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("inbox")]
    public async Task<IActionResult> Inbox(FederationEnvelope envelope, CancellationToken ct)
    {
        try
        {
            await _federation.ReceiveAsync(envelope, ct);
            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
