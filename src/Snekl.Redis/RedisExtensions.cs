using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Snekl.Redis;

public static class RedisExtensions
{
    public static bool IsRedisConfigured(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config.GetConnectionString("Redis"));

    public static IServiceCollection AddSneklRedis(this IServiceCollection services, IConfiguration config, string instanceName = "snekl")
    {
        services.Configure<RateLimitOptions>(config.GetSection(RateLimitOptions.SectionName));

        var connectionString = config.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDistributedMemoryCache();
            return services;
        }

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = connectionString;
            options.InstanceName = instanceName;
        });

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionString));
        services.AddSingleton<IRateLimitService, RedisRateLimitService>();
        services.AddSingleton<IRedisDedupService, RedisDedupService>();

        return services;
    }

    public static IApplicationBuilder UseSneklRateLimiting(this IApplicationBuilder app, IConfiguration config)
    {
        if (!IsRedisConfigured(config)) return app;
        return app.UseMiddleware<RateLimitMiddleware>();
    }
}
