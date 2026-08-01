using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Snekl.Infrastructure.Caching;

public static class DistributedCacheExtensions
{
    public static async Task<T?> GetJsonAsync<T>(this IDistributedCache cache, string key, CancellationToken ct = default)
    {
        var json = await cache.GetStringAsync(key, ct);
        return json is null ? default : JsonSerializer.Deserialize<T>(json);
    }

    public static Task SetJsonAsync<T>(
        this IDistributedCache cache,
        string key,
        T value,
        TimeSpan ttl,
        CancellationToken ct = default) =>
        cache.SetStringAsync(key, JsonSerializer.Serialize(value), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        }, ct);
}

public static class CacheKeys
{
    public static string Jwks(string issuer) => $"jwks:{issuer.Trim().TrimEnd('/')}";
    public static string ShadowUser(string issuer, Guid subjectId) => $"shadow:{issuer.Trim().TrimEnd('/')}:{subjectId}";
    public static string Presentation(Guid userId, Guid? viewerId) => $"pres:{userId}:{viewerId?.ToString() ?? "anon"}";
    public static string FederationEnvelope(Guid sourceNodeId, string type, Guid entityId) =>
        $"fed:{sourceNodeId}:{type}:{entityId}";
    public static string DidWebDocument(string did) => $"didweb:{did}";
    public static string VouchNonce(string signature) => $"vouch:{signature[..Math.Min(32, signature.Length)]}";
}
