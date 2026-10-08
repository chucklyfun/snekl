using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Search;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;
using Snekl.Infrastructure.Storage;
using Snekl.Redis;

namespace Snekl.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSneklInfrastructure(this IServiceCollection services, string connectionString, IConfiguration config)
    {
        services.AddScoped<ICurrentServer, CurrentServer>();
        services.AddDbContext<SneklDbContext>(options =>
            options.UseNpgsql(connectionString));
        services.AddScoped<IVirtualServerService, VirtualServerService>();
        services.AddHttpClient<IBusinessHostingAuthClient, BusinessHostingAuthClient>();
        services.AddScoped<IBusinessServerService, BusinessServerService>();

        services.AddSneklRedis(config, config["Node:Name"] ?? "snekl-content");

        services.Configure<ObjectStorageOptions>(config.GetSection(ObjectStorageOptions.SectionName));
        if (string.Equals(config["ObjectStorage:Provider"], "InMemory", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IObjectStorage, InMemoryObjectStorage>();
        else
            services.AddSingleton<IObjectStorage, S3ObjectStorage>();

        services.AddScoped<IUserPresentationService, UserPresentationService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<ICuratedListService, CuratedListService>();
        services.AddScoped<IOwnershipService, OwnershipService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IForumService, ForumService>();
        services.AddScoped<INestMapService, NestMapService>();
        services.AddScoped<INestTransferService, NestTransferService>();
        services.AddHttpClient<IFollowGraphClient, FollowGraphClient>();
        services.AddScoped<IPostService, PostService>();
        AddPostSearch(services, config);
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IPostUpvoteService, PostUpvoteService>();
        services.AddScoped<IEmoteService, EmoteService>();
        services.AddHttpClient<IPostAggregationService, PostAggregationService>();
        services.AddScoped<IFederationEnvelopeValidator, FederationEnvelopeValidator>();
        services.AddHttpClient<INodeDidResolver, NodeDidResolver>();
        services.AddScoped<INodeService, NodeService>();
        services.AddScoped<IFederationService, FederationService>();
        services.AddScoped<IPersonClusterMappingService, PersonClusterMappingService>();
        services.AddScoped<ISiteAdminAuthService, SiteAdminAuthService>();
        services.AddScoped<ISuperUserService, SuperUserService>();
        services.AddScoped<IHubInviteService, HubInviteService>();
        services.AddScoped<INsfwAgeCheckService, NsfwAgeCheckService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IChangeLogService, ChangeLogService>();
        services.AddScoped<IRoleSeedService, RoleSeedService>();
        services.AddScoped<IServerRoleService, ServerRoleService>();
        services.AddScoped<IForumRoleService, ForumRoleService>();
        services.AddScoped<IForumMemberService, ForumMemberService>();
        services.AddScoped<IForumModerationService, ForumModerationService>();
        services.AddHttpClient<IFederationClient, FederationClient>();
        services.AddScoped<IExtensionInstallService, ExtensionInstallService>();
        services.AddScoped<IWatercoolerService, WatercoolerService>();
        services.AddScoped<IForumPageService, ForumPageService>();
        services.AddScoped<IDirectConversationService, DirectConversationService>();
        services.AddHttpClient("fcm");
        services.AddSingleton<IReplyAlertPushSender, FcmReplyAlertPushSender>();
        services.AddScoped<IReplyAlertService, ReplyAlertService>();
        services.AddScoped<IContentBlockCatalogService, ContentBlockCatalogService>();
        services.AddScoped<IHubFeedPublisher, HubFeedPublisher>();
        services.AddHostedService<HubOutboxDispatcher>();
        services.AddSneklAuthentication(config);
        services.AddSiteAdminAuthorization();

        return services;
    }

    private static void AddPostSearch(IServiceCollection services, IConfiguration config)
    {
        services.Configure<OpenSearchOptions>(config.GetSection(OpenSearchOptions.SectionName));
        var url = config.GetConnectionString("OpenSearch");
        if (string.IsNullOrWhiteSpace(url))
        {
            services.AddSingleton<IPostSearchIndex, NullPostSearchIndex>();
            return;
        }

        services.AddHttpClient<IPostSearchIndex, OpenSearchPostIndex>(client =>
        {
            client.BaseAddress = new Uri(url.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHostedService<PostSearchBootstrap>();
    }
}
