using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/node")]
public class NodeController : ControllerBase
{
    private readonly INodeService _node;

    public NodeController(INodeService node) => _node = node;

    [HttpGet]
    public async Task<ActionResult<NodeInfoDto>> Get(CancellationToken ct)
    {
        var node = await _node.GetOrCreateAsync(ct);
        return Ok(new NodeInfoDto(node.Id, node.Name, node.BaseUrl, node.Description, node.CreatedAt));
    }

    [HttpGet(".well-known/snekl")]
    public Task<ActionResult<NodeInfoDto>> WellKnown(CancellationToken ct) => Get(ct);
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
        await _federation.ReceiveAsync(envelope, ct);
        return Accepted();
    }
}
