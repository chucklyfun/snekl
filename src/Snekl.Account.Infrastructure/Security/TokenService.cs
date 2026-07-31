using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Snekl.Account.Domain.Entities;

namespace Snekl.Account.Infrastructure.Security;

public interface ITokenService
{
    string IssueToken(AuthIdentity identity, PersonCluster cluster, string issuerUrl, TimeSpan? lifetime = null);
    string GetSigningKey();
    SymmetricSecurityKey GetValidationKey();
}

public class TokenService : ITokenService
{
    private readonly SymmetricSecurityKey _key;
    private readonly SigningCredentials _credentials;
    private readonly string _signingKey;

    public TokenService(IConfiguration config)
    {
        _signingKey = config["Auth:SigningKey"] ?? Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey));
        _credentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
    }

    public string GetSigningKey() => _signingKey;

    public SymmetricSecurityKey GetValidationKey() => _key;

    public string IssueToken(AuthIdentity identity, PersonCluster cluster, string issuerUrl, TimeSpan? lifetime = null)
    {
        var expiry = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromDays(7));
        var token = new JwtSecurityToken(
            issuer: issuerUrl.TrimEnd('/'),
            audience: "snekl",
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, identity.Id.ToString()),
                new Claim("cluster_id", cluster.Id.ToString()),
                new Claim("username", cluster.CanonicalUsername),
                new Claim("display_name", cluster.DisplayName),
                new Claim(JwtRegisteredClaimNames.Email, cluster.Email)
            },
            expires: expiry,
            signingCredentials: _credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
