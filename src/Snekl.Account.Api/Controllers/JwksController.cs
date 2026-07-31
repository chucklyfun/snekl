using Microsoft.AspNetCore.Mvc;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Security;

namespace Snekl.Account.Api.Controllers;

[ApiController]
public class JwksController : ControllerBase
{
    private readonly ITokenService _tokens;

    public JwksController(ITokenService tokens) => _tokens = tokens;

    [HttpGet(".well-known/jwks.json")]
    public ActionResult<JwksResponse> GetJwks()
    {
        var keyBytes = _tokens.GetValidationKey().Key;
        var kid = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(keyBytes)).ToLowerInvariant()[..16];
        return Ok(new JwksResponse([
            new JwkKey("oct", "sig", kid, "HS256", Convert.ToBase64String(keyBytes), "")
        ]));
    }
}
