using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Security;

/// <summary>
/// Works out which server a request is for while the bearer token is still being validated.
/// Authentication runs before VirtualServerMiddleware and in its own DI scope, so without this
/// issuer trust and shadow-user lookups would span every server on the host.
/// The rules match VirtualServerMiddleware: an X-Snekl-Server header picks an enabled server;
/// with no header, a host with exactly one enabled server uses it. Anything else is left
/// unresolved, and the middleware then rejects the request as before.
/// </summary>
public static class RequestServerScope
{
    public const string HeaderName = "X-Snekl-Server";

    private const string ItemKey = "Snekl.RequestServerScope";

    public static Guid? Resolve(HttpContext? http, SneklDbContext db)
    {
        if (http is null)
            return null;
        if (http.Items.TryGetValue(ItemKey, out var cached))
            return cached as Guid?;

        Guid? resolved = null;
        var header = http.Request.Headers[HeaderName].FirstOrDefault()?.Trim();
        if (!string.IsNullOrEmpty(header))
        {
            if (Guid.TryParse(header, out var id) &&
                db.NodeConfig.AsNoTracking().Any(n => n.Id == id && n.DisabledAt == null))
                resolved = id;
        }
        else
        {
            var enabled = db.NodeConfig.AsNoTracking()
                .Where(n => n.DisabledAt == null)
                .OrderBy(n => n.CreatedAt)
                .Select(n => n.Id)
                .Take(2)
                .ToList();
            if (enabled.Count == 1)
                resolved = enabled[0];
        }

        http.Items[ItemKey] = resolved;
        return resolved;
    }

    /// <summary>Scope a freshly created service scope to the request's server, when known.</summary>
    public static void Apply(HttpContext? http, IServiceProvider services)
    {
        var db = services.GetRequiredService<SneklDbContext>();
        if (Resolve(http, db) is Guid serverId)
            services.GetRequiredService<ICurrentServer>().Use(serverId);
    }
}
