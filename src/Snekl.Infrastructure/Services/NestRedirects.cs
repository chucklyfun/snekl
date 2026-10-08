using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public static class NestRedirects
{
    public static async Task RememberAsync(
        SneklDbContext db,
        Guid serverId,
        string? topSlug,
        string slug,
        Guid targetForumId,
        CancellationToken ct)
    {
        var top = (topSlug ?? string.Empty).Trim().ToLowerInvariant();
        var normalized = slug.Trim().ToLowerInvariant();
        var existing = await db.ForumRedirects.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.ServerId == serverId && r.TopSlug == top && r.Slug == normalized, ct);
        if (existing is null)
        {
            db.ForumRedirects.Add(new ForumRedirect
            {
                ServerId = serverId,
                TopSlug = top,
                Slug = normalized,
                TargetForumId = targetForumId
            });
            return;
        }

        existing.TargetForumId = targetForumId;
        existing.UpdatedAt = DateTime.UtcNow;
    }

    public static async Task ForgetAddressAsync(
        SneklDbContext db,
        Guid serverId,
        string? topSlug,
        string slug,
        CancellationToken ct)
    {
        var top = (topSlug ?? string.Empty).Trim().ToLowerInvariant();
        var normalized = slug.Trim().ToLowerInvariant();
        var rows = await db.ForumRedirects.IgnoreQueryFilters()
            .Where(r => r.ServerId == serverId && r.TopSlug == top && r.Slug == normalized)
            .ToListAsync(ct);
        db.ForumRedirects.RemoveRange(rows);
    }
}
