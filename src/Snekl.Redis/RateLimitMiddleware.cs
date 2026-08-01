using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Snekl.Redis;

public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRateLimitService _rateLimit;
    private readonly RateLimitOptions _options;
    private readonly ILogger<RateLimitMiddleware> _logger;

    public RateLimitMiddleware(
        RequestDelegate next,
        IRateLimitService rateLimit,
        IOptions<RateLimitOptions> options,
        ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _rateLimit = rateLimit;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled || _options.Rules.Count == 0)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method.ToUpperInvariant();
        var rule = _options.Rules.FirstOrDefault(r =>
            method == r.Method.ToUpperInvariant() &&
            path.StartsWith(r.PathPrefix, StringComparison.OrdinalIgnoreCase));

        if (rule is null)
        {
            await _next(context);
            return;
        }

        var clientKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var bucket = $"{rule.PathPrefix}:{clientKey}";
        var result = await _rateLimit.CheckAsync(bucket, rule.Limit, TimeSpan.FromSeconds(rule.WindowSeconds));

        context.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();
        if (!result.Allowed)
        {
            _logger.LogWarning("Rate limit exceeded for {Bucket} on {Path}", bucket, path);
            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            context.Response.Headers["Retry-After"] = result.RetryAfterSeconds.ToString();
            await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded. Try again later." });
            return;
        }

        await _next(context);
    }
}
