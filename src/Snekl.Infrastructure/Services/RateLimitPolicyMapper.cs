using Snekl.Domain.Dtos;
using Snekl.Redis;

namespace Snekl.Infrastructure.Services;

public static class RateLimitPolicyMapper
{
    public const string PartitionIp = "ip";

    public static RateLimitPolicyDto ToDto(RateLimitOptions options, bool redisConfigured) =>
        new(
            options.Enabled && redisConfigured,
            PartitionIp,
            options.Rules
                .Select(r => new RateLimitRuleDto(
                    r.PathPrefix,
                    string.IsNullOrWhiteSpace(r.Method) ? "POST" : r.Method.ToUpperInvariant(),
                    r.Limit,
                    r.WindowSeconds))
                .ToList());
}
