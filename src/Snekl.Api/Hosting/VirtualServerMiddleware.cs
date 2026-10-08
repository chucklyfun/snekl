using Microsoft.EntityFrameworkCore;
using Snekl.Infrastructure.Data;

namespace Snekl.Api.Hosting;

public sealed class VirtualServerMiddleware
{
    public const string HeaderName = "X-Snekl-Server";

    private readonly RequestDelegate _next;

    public VirtualServerMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, SneklDbContext db, ICurrentServer current)
    {
        if (IsExempt(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var header = context.Request.Headers[HeaderName].FirstOrDefault()?.Trim();
        // Login lists every auth this host trusts, before a virtual server is chosen.
        if (IsAuthCatalog(context.Request.Path) && string.IsNullOrEmpty(header))
        {
            await _next(context);
            return;
        }
        if (!string.IsNullOrEmpty(header))
        {
            if (!Guid.TryParse(header, out var id))
            {
                await RejectAsync(context, StatusCodes.Status400BadRequest, "X-Snekl-Server must be a server id.");
                return;
            }

            var node = await db.NodeConfig.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, context.RequestAborted);
            if (node is null)
            {
                await RejectAsync(context, StatusCodes.Status404NotFound, "Server not found.");
                return;
            }

            if (node.DisabledAt is not null)
            {
                await RejectAsync(context, StatusCodes.Status403Forbidden, "Server is disabled.");
                return;
            }

            current.Use(node.Id);
            await _next(context);
            return;
        }

        var enabled = await db.NodeConfig.AsNoTracking()
            .Where(n => n.DisabledAt == null)
            .OrderBy(n => n.CreatedAt)
            .Select(n => n.Id)
            .Take(2)
            .ToListAsync(context.RequestAborted);

        if (enabled.Count == 1)
        {
            current.Use(enabled[0]);
            await _next(context);
            return;
        }

        if (enabled.Count == 0)
        {
            await _next(context);
            return;
        }

        await RejectAsync(context, StatusCodes.Status400BadRequest, "X-Snekl-Server header is required.");
    }

    private static bool IsExempt(PathString path) =>
        path.StartsWithSegments("/api/admin/host") ||
        path.StartsWithSegments("/server") ||
        path.StartsWithSegments("/swagger");

    private static bool IsAuthCatalog(PathString path) =>
        path.StartsWithSegments("/api/node/auth");

    private static async Task RejectAsync(HttpContext context, int status, string error)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error });
    }
}
