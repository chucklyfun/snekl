using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Security;

public class ExternalAuthClaimsTransformation : IClaimsTransformation
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ExternalAuthClaimsTransformation(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true) return principal;
        if (principal.HasClaim("auth_source", "shadow")) return principal;
        if (SiteAdminAuthorization.IsSiteAdmin(principal)) return principal;

        var did = principal.FindFirstValue("did");
        if (string.IsNullOrWhiteSpace(did)) return principal;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var externalAuth = scope.ServiceProvider.GetRequiredService<IExternalAuthService>();
        var user = await externalAuth.GetOrCreateShadowUserAsync(principal);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("did", did),
            new("auth_source", "shadow")
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new Claim(ClaimTypes.Email, user.Email));

        var identity = new ClaimsIdentity(claims, principal.Identity.AuthenticationType);
        return new ClaimsPrincipal(identity);
    }
}

public static class AuthenticationExtensions
{
    public static IServiceCollection AddSneklAuthentication(this IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<IExternalAuthService, ExternalAuthService>();
        services.AddScoped<IClaimsTransformation, ExternalAuthClaimsTransformation>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IServiceScopeFactory>((options, scopeFactory) =>
            {
                // Keep JWT claim names stable; site admin checks both "role" and ClaimTypes.Role.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidAudiences = ["snekl", SiteAdminClaims.Audience],
                    IssuerSigningKeyResolver = (_, securityToken, _, _) =>
                    {
                        // JwtBearer may pass JsonWebToken (default) or JwtSecurityToken.
                        // Use the base SecurityToken.Issuer so both work.
                        if (securityToken is null || string.IsNullOrWhiteSpace(securityToken.Issuer))
                            return [];

                        using var scope = scopeFactory.CreateScope();
                        var externalAuth = scope.ServiceProvider.GetRequiredService<IExternalAuthService>();
                        return externalAuth.ResolveSigningKeysAsync(securityToken.Issuer).GetAwaiter().GetResult().ToList();
                    },
                    IssuerValidator = (issuer, _, _) =>
                    {
                        using var scope = scopeFactory.CreateScope();
                        var externalAuth = scope.ServiceProvider.GetRequiredService<IExternalAuthService>();
                        if (!externalAuth.IsTrustedIssuer(issuer))
                            throw new SecurityTokenInvalidIssuerException($"Untrusted issuer: {issuer}");
                        return issuer;
                    }
                };
            });

        return services;
    }
}
