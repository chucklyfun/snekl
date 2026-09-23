using System.Collections.Concurrent;
using System.Text.Json;

namespace Snekl.Faker;

/// <summary>
/// Paces Content POSTs using the node's advertised <c>rateLimiting</c> policy
/// (from OAuth tokens or GET /api/node) so extensions stay under IP buckets.
/// </summary>
public sealed class FakerApiLimits
{
    private readonly ConcurrentDictionary<string, HostState> _hosts = new(StringComparer.OrdinalIgnoreCase);

    public void Remember(string contentBaseUrl, JsonElement source)
    {
        var policy = TryParsePolicy(source);
        if (policy is null)
            return;
        var host = State(contentBaseUrl);
        host.Policy = policy;
        host.FetchedAt = DateTime.UtcNow;
    }

    public async Task WaitAsync(
        string contentBaseUrl,
        string method,
        string path,
        Func<CancellationToken, Task<JsonElement>> loadNode,
        CancellationToken ct)
    {
        var host = State(contentBaseUrl);
        await host.Gate.WaitAsync(ct);
        try
        {
            await EnsurePolicyAsync(host, loadNode, ct);
            var policy = host.Policy;
            if (policy is null || !policy.Enabled)
                return;

            var rule = Match(policy, method, path);
            if (rule is null)
                return;

            var key = $"{rule.Method}:{rule.PathPrefix}";
            if (!host.Windows.TryGetValue(key, out var hits))
            {
                hits = new Queue<DateTime>();
                host.Windows[key] = hits;
            }

            var window = TimeSpan.FromSeconds(Math.Max(1, rule.WindowSeconds));
            Trim(hits, window);
            while (hits.Count >= rule.Limit)
            {
                var wait = hits.Peek() + window - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, ct);
                Trim(hits, window);
            }

            hits.Enqueue(DateTime.UtcNow);
        }
        finally
        {
            host.Gate.Release();
        }
    }

    public async Task DelayRetryAfterAsync(int retryAfterSeconds, CancellationToken ct)
    {
        var seconds = Math.Clamp(retryAfterSeconds, 1, 120);
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
    }

    private async Task EnsurePolicyAsync(HostState host, Func<CancellationToken, Task<JsonElement>> loadNode, CancellationToken ct)
    {
        if (host.Policy is not null && DateTime.UtcNow - host.FetchedAt < TimeSpan.FromMinutes(5))
            return;

        try
        {
            var node = await loadNode(ct);
            var policy = TryParsePolicy(node);
            if (policy is null)
                return;
            host.Policy = policy;
            host.FetchedAt = DateTime.UtcNow;
        }
        catch
        {
            // Keep posting; 429 Retry-After is the fallback.
        }
    }

    private HostState State(string contentBaseUrl) =>
        _hosts.GetOrAdd(FakerTargets.Normalize(contentBaseUrl), _ => new HostState());

    private static RateLimitPolicy? TryParsePolicy(JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Object)
            return null;
        if (!source.TryGetProperty("rateLimiting", out var raw) || raw.ValueKind != JsonValueKind.Object)
            raw = source;
        if (raw.ValueKind != JsonValueKind.Object)
            return null;
        if (!raw.TryGetProperty("enabled", out var enabledEl) || !raw.TryGetProperty("rules", out var rulesEl))
            return null;

        var rules = new List<RateLimitRule>();
        if (rulesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in rulesEl.EnumerateArray())
            {
                var prefix = item.TryGetProperty("pathPrefix", out var p) ? p.GetString() : null;
                var method = item.TryGetProperty("method", out var m) ? m.GetString() : "POST";
                if (string.IsNullOrWhiteSpace(prefix))
                    continue;
                var limit = item.TryGetProperty("limit", out var l) && l.TryGetInt32(out var li) ? li : 0;
                var window = item.TryGetProperty("windowSeconds", out var w) && w.TryGetInt32(out var wi) ? wi : 60;
                if (limit <= 0)
                    continue;
                rules.Add(new RateLimitRule(prefix, (method ?? "POST").ToUpperInvariant(), limit, window));
            }
        }

        var partition = raw.TryGetProperty("partition", out var part) ? part.GetString() ?? "ip" : "ip";
        return new RateLimitPolicy(enabledEl.ValueKind == JsonValueKind.True, partition, rules);
    }

    private static RateLimitRule? Match(RateLimitPolicy policy, string method, string path)
    {
        var verb = method.ToUpperInvariant();
        return policy.Rules.FirstOrDefault(r =>
            string.Equals(r.Method, verb, StringComparison.OrdinalIgnoreCase) &&
            path.StartsWith(r.PathPrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static void Trim(Queue<DateTime> hits, TimeSpan window)
    {
        var cutoff = DateTime.UtcNow - window;
        while (hits.Count > 0 && hits.Peek() < cutoff)
            hits.Dequeue();
    }

    private sealed class HostState
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public RateLimitPolicy? Policy;
        public DateTime FetchedAt;
        public readonly Dictionary<string, Queue<DateTime>> Windows = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record RateLimitPolicy(bool Enabled, string Partition, IReadOnlyList<RateLimitRule> Rules);
    private sealed record RateLimitRule(string PathPrefix, string Method, int Limit, int WindowSeconds);
}
