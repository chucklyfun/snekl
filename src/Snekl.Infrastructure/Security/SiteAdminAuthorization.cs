using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;

namespace Snekl.Infrastructure.Security;

public static class SiteAdminClaims
{
    public const string Role = "role";
    public const string SiteAdmin = "site_admin";
    public const string Audience = "snekl-admin";
}

public static class SiteAdminAuthorization
{
    public const string SiteAdminPolicy = "SiteAdmin";
    public const string EndUserPolicy = "EndUser";

    public static bool IsSiteAdmin(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true &&
        user.HasClaim(SiteAdminClaims.Role, SiteAdminClaims.SiteAdmin);

    public static IServiceCollection AddSiteAdminAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(SiteAdminPolicy, policy =>
                policy.RequireClaim(SiteAdminClaims.Role, SiteAdminClaims.SiteAdmin));

            options.AddPolicy(EndUserPolicy, policy =>
                policy.RequireAssertion(ctx =>
                    ctx.User.Identity?.IsAuthenticated == true &&
                    !IsSiteAdmin(ctx.User)));
        });

        return services;
    }
}
