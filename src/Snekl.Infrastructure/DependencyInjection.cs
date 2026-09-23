using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;
using Snekl.Infrastructure.Storage;
using Snekl.Redis;

namespace Snekl.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSneklInfrastructure(this IServiceCollection services, string connectionString, IConfiguration config)
    {
        services.AddDbContext<SneklDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddSneklRedis(config, config["Node:Name"] ?? "snekl-content");

        services.Configure<ObjectStorageOptions>(config.GetSection(ObjectStorageOptions.SectionName));
        if (string.Equals(config["ObjectStorage:Provider"], "InMemory", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IObjectStorage, InMemoryObjectStorage>();
        else
            services.AddSingleton<IObjectStorage, S3ObjectStorage>();

        services.AddScoped<IUserPresentationService, UserPresentationService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<ICuratedListService, CuratedListService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IForumService, ForumService>();
        services.AddScoped<IPostService, PostService>();
        services.AddScoped<IPostUpvoteService, PostUpvoteService>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IEmoteService, EmoteService>();
        services.AddHttpClient<IPostAggregationService, PostAggregationService>();
        services.AddScoped<IFederationEnvelopeValidator, FederationEnvelopeValidator>();
        services.AddHttpClient<INodeDidResolver, NodeDidResolver>();
        services.AddScoped<INodeService, NodeService>();
        services.AddScoped<IFederationService, FederationService>();
        services.AddScoped<IPersonClusterMappingService, PersonClusterMappingService>();
        services.AddScoped<ISiteAdminAuthService, SiteAdminAuthService>();
        services.AddScoped<ISuperUserService, SuperUserService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IChangeLogService, ChangeLogService>();
        services.AddScoped<IRoleSeedService, RoleSeedService>();
        services.AddScoped<IServerRoleService, ServerRoleService>();
        services.AddScoped<IForumRoleService, ForumRoleService>();
        services.AddScoped<IForumMemberService, ForumMemberService>();
        services.AddScoped<IForumModerationService, ForumModerationService>();
        services.AddScoped<IWatercoolerService, WatercoolerService>();
        services.AddHttpClient<IFederationClient, FederationClient>();
        services.AddSneklAuthentication(config);
        services.AddSiteAdminAuthorization();

        return services;
    }
}
