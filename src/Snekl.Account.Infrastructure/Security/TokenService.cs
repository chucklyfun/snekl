using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Snekl.Account.Domain.Entities;
using Snekl.Domain;

namespace Snekl.Account.Infrastructure.Security;

public interface ITokenService
{
    string IssueToken(AuthIdentity identity, PersonCluster cluster, string issuerUrl, TokenIssueOptions? options = null);
    string GetSigningKey();
    SymmetricSecurityKey GetValidationKey();
}

public sealed record TokenIssueOptions(
    TimeSpan? Lifetime = null,
    string? Azp = null,
    bool Bot = false,
    Guid? InstallId = null,
    string TokenUse = OAuthClaims.TokenUseUser,
    string? ContentBaseUrl = null,
    DateTimeOffset? AuthTime = null);

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

    public string IssueToken(AuthIdentity identity, PersonCluster cluster, string issuerUrl, TokenIssueOptions? options = null)
    {
        options ??= new TokenIssueOptions();
        var expiry = DateTime.UtcNow.Add(options.Lifetime ?? TimeSpan.FromDays(7));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, identity.Id.ToString()),
            new("cluster_id", cluster.Id.ToString()),
            new("did", cluster.Did),
            new("username", cluster.CanonicalUsername),
            new("display_name", cluster.DisplayName),
            new(JwtRegisteredClaimNames.Email, cluster.Email),
            new(OAuthClaims.TokenUse, options.TokenUse)
        };

        if (!string.IsNullOrWhiteSpace(options.Azp))
            claims.Add(new Claim(OAuthClaims.Azp, options.Azp));
        if (options.Bot)
            claims.Add(new Claim(OAuthClaims.Bot, "true"));
        if (options.InstallId.HasValue)
            claims.Add(new Claim(OAuthClaims.InstallId, options.InstallId.Value.ToString("D")));
        if (!string.IsNullOrWhiteSpace(options.ContentBaseUrl))
            claims.Add(new Claim(OAuthClaims.ContentBaseUrl, options.ContentBaseUrl.TrimEnd('/')));
        if (options.AuthTime.HasValue)
            claims.Add(new Claim(OAuthClaims.AuthTime, options.AuthTime.Value.ToUnixTimeSeconds().ToString()));

        var token = new JwtSecurityToken(
            issuer: issuerUrl.TrimEnd('/'),
            audience: "snekl",
            claims: claims,
            expires: expiry,
            signingCredentials: _credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
