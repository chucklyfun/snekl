using StackExchange.Redis;

namespace Snekl.Redis;

public class RedisRateLimitService : IRateLimitService
{
    private readonly IConnectionMultiplexer _redis;

    public RedisRateLimitService(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<RateLimitResult> CheckAsync(string bucketKey, int limit, TimeSpan window, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var windowId = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / Math.Max(1, (long)window.TotalSeconds);
        var redisKey = $"rl:{bucketKey}:{windowId}";

        var count = await db.StringIncrementAsync(redisKey);
        if (count == 1)
            await db.KeyExpireAsync(redisKey, window);

        var remaining = Math.Max(0, limit - (int)count);
        var retryAfter = count > limit ? (int)window.TotalSeconds : 0;
        return new RateLimitResult(count <= limit, remaining, retryAfter);
    }
}

public class RedisDedupService : IRedisDedupService
{
    private readonly IConnectionMultiplexer _redis;

    public RedisDedupService(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<bool> TryMarkProcessedAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        return await db.StringSetAsync($"dedup:{key}", "1", ttl, When.NotExists);
    }
}
