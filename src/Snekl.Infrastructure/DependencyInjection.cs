using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;
using Snekl.Redis;

namespace Snekl.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSneklInfrastructure(this IServiceCollection services, string connectionString, IConfiguration config)
    {
        services.AddDbContext<SneklDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddSneklRedis(config, config["Node:Name"] ?? "snekl-content");

        services.AddScoped<IUserPresentationService, UserPresentationService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<ICuratedListService, CuratedListService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IForumService, ForumService>();
        services.AddScoped<IPostService, PostService>();
        services.AddScoped<INodeService, NodeService>();
        services.AddScoped<IFederationService, FederationService>();
        services.AddScoped<IPersonClusterMappingService, PersonClusterMappingService>();
        services.AddScoped<ISiteAdminAuthService, SiteAdminAuthService>();
        services.AddHttpClient<IFederationClient, FederationClient>();
        services.AddSneklAuthentication(config);
        services.AddSiteAdminAuthorization();

        return services;
    }
}
