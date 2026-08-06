using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Services;
using Snekl.Domain.Identity.Did;

namespace Snekl.Account.Api.Controllers;

[ApiController]
public class DidController : ControllerBase
{
    private readonly IAuthServerService _server;

    public DidController(IAuthServerService server) => _server = server;

    [HttpGet(".well-known/did.json")]
    public async Task<ActionResult<DidDocument>> AuthServerDidDocument(CancellationToken ct)
    {
        var config = await _server.GetOrCreateAsync(ct);
        var vmId = $"{config.VerificationDid}#{config.VerificationKeyMultibase[1..]}";

        return Ok(new DidDocument(
            [
                "https://www.w3.org/ns/did/v1",
                "https://w3id.org/security/suites/ed25519-2020/v1"
            ],
            config.Did,
            [new DidVerificationMethod(vmId, "Ed25519VerificationKey2020", config.Did, config.VerificationKeyMultibase)],
            [vmId],
            [vmId]));
    }

    [HttpGet("api/did/resolve/{*did}")]
    public ActionResult<DidDocument> Resolve(string did)
    {
        if (!did.StartsWith(DidKey.Prefix, StringComparison.Ordinal))
            return BadRequest(new { error = "Only did:key resolution is supported inline." });

        return Ok(DidDocumentBuilder.ForDidKey(did));
    }
}
