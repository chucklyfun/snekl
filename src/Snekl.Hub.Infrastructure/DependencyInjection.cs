using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Snekl.Hub.Infrastructure.Data;
using Snekl.Hub.Infrastructure.Services;
using Snekl.Redis;

namespace Snekl.Hub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddHubInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration config)
    {
        services.AddDbContext<HubDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddSneklRedis(config, config["Hub:Name"] ?? "snekl-hub");
        services.AddScoped<IFederationRegistry, FederationRegistry>();
        services.AddHttpClient<IOriginJoinClient, OriginJoinClient>();
        services.AddHttpClient<IOriginRecentClient, OriginRecentClient>(client =>
            client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<IHubFeedService, HubFeedService>();
        services.AddHttpClient<IOriginVerificationKeyResolver, OriginVerificationKeyResolver>(client =>
            client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<IHubIngestService, HubIngestService>();
        services.AddScoped<IHubFeedOriginSync, HubFeedOriginSync>();
        services.AddScoped<IHubOperatorService, HubOperatorService>();

        return services;
    }
}
