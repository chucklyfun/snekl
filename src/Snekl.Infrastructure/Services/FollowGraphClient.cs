using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Snekl.Infrastructure.Services;

public sealed record FollowWatcher(string FollowerDid, string AlertMode);

public interface IFollowGraphClient
{
    Task<bool> FollowsAsync(string? issuerUrl, string followerDid, string targetDid, CancellationToken ct = default);
    Task<IReadOnlyList<FollowWatcher>> ListWatchersAsync(string? issuerUrl, string targetDid, CancellationToken ct = default);
}

public class FollowGraphClient : IFollowGraphClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ILogger<FollowGraphClient> _logger;
    private readonly ConcurrentDictionary<string, bool> _cache = new(StringComparer.Ordinal);

    public FollowGraphClient(HttpClient http, ILogger<FollowGraphClient> logger)
    {
        _http = http;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(5);
    }

    public async Task<bool> FollowsAsync(
        string? issuerUrl,
        string followerDid,
        string targetDid,
        CancellationToken ct = default)
    {
        var issuer = (issuerUrl ?? "").TrimEnd('/');
        var follower = (followerDid ?? "").Trim();
        var target = (targetDid ?? "").Trim();
        if (issuer.Length == 0 || follower.Length == 0 || target.Length == 0)
            return false;

        var cacheKey = $"{issuer}\n{follower}\n{target}";
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var url =
                $"{issuer}/api/follows/contains?followerDid={Uri.EscapeDataString(follower)}&targetDid={Uri.EscapeDataString(target)}";
            var result = await _http.GetFromJsonAsync<FollowContainsResponse>(url, JsonOptions, ct);
            var follows = result?.Follows == true;
            _cache[cacheKey] = follows;
            return follows;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check whether {FollowerDid} follows {TargetDid} at {Issuer}", follower, target, issuer);
            return false;
        }
    }

    public async Task<IReadOnlyList<FollowWatcher>> ListWatchersAsync(
        string? issuerUrl,
        string targetDid,
        CancellationToken ct = default)
    {
        var issuer = (issuerUrl ?? "").TrimEnd('/');
        var target = (targetDid ?? "").Trim();
        if (issuer.Length == 0 || target.Length == 0)
            return [];

        try
        {
            var url = $"{issuer}/api/follows/watchers?targetDid={Uri.EscapeDataString(target)}";
            var result = await _http.GetFromJsonAsync<List<FollowWatcherResponse>>(url, JsonOptions, ct);
            if (result is null)
                return [];
            return result
                .Where(row => !string.IsNullOrWhiteSpace(row.FollowerDid))
                .Select(row => new FollowWatcher(row.FollowerDid, row.AlertMode ?? ""))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list follow alerts for {TargetDid} at {Issuer}", target, issuer);
            return [];
        }
    }

    private sealed record FollowContainsResponse(bool Follows);

    private sealed record FollowWatcherResponse(string FollowerDid, string? AlertMode);
}
