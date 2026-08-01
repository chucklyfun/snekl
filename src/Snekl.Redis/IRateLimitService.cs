namespace Snekl.Redis;

public record RateLimitResult(bool Allowed, int Remaining, int RetryAfterSeconds);

public interface IRateLimitService
{
    Task<RateLimitResult> CheckAsync(string bucketKey, int limit, TimeSpan window, CancellationToken ct = default);
}

public interface IRedisDedupService
{
    Task<bool> TryMarkProcessedAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}
