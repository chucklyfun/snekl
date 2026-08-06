using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Snekl.Domain.Identity.Did;
using Snekl.Infrastructure.Caching;

namespace Snekl.Infrastructure.Services;

public interface INodeDidResolver
{
    Task<string?> ResolveVerificationKeyMultibaseAsync(string did, string baseUrl, CancellationToken ct = default);
}

public class NodeDidResolver : INodeDidResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly IDistributedCache _cache;

    public NodeDidResolver(HttpClient http, IDistributedCache cache)
    {
        _http = http;
        _cache = cache;
    }

    public async Task<string?> ResolveVerificationKeyMultibaseAsync(string did, string baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(did) || string.IsNullOrWhiteSpace(baseUrl)) return null;

        var cacheKey = CacheKeys.DidWebDocument(did);
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (!string.IsNullOrEmpty(cached))
        {
            var doc = JsonSerializer.Deserialize<DidDocument>(cached, JsonOptions);
            return doc?.VerificationMethod.FirstOrDefault()?.PublicKeyMultibase;
        }

        var url = DidWeb.DocumentUrl(baseUrl);
        try
        {
            var doc = await _http.GetFromJsonAsync<DidDocument>(url, JsonOptions, ct);
            var key = doc?.VerificationMethod.FirstOrDefault()?.PublicKeyMultibase;
            if (doc is not null && !string.IsNullOrWhiteSpace(key))
            {
                await _cache.SetStringAsync(
                    cacheKey,
                    JsonSerializer.Serialize(doc, JsonOptions),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) },
                    ct);
            }

            return key;
        }
        catch
        {
            return null;
        }
    }
}
