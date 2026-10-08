using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Snekl.Domain.Entities;
using Snekl.Domain.Identity;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

/// <summary>
/// Effective auth issuers and hubs for the current server, worked out when they are read.
/// A spawned server uses the host's active list (unless it opts out) plus its own rows. Its own
/// row for an issuer URL takes precedence over the host's, and a required (business) issuer is
/// always included. The host is the first server on the deployment. Without a server scope the
/// lists span every server, as before.
/// </summary>
public static class ServerTrust
{
    public static List<TrustedAuthIssuer> EffectiveTrustedIssuers(this SneklDbContext db)
    {
        if (!db.HasServerScope)
            return db.TrustedAuthIssuers.AsNoTracking().Where(i => i.IsActive || i.IsRequired).ToList();

        var serverId = db.ScopedServerId;
        var hostId = HostQuery(db).FirstOrDefault();
        var inherit = InheritsQuery(db, serverId, n => n.InheritHostAuthIssuers).FirstOrDefault() ?? true;
        var own = OwnIssuersQuery(db, serverId).ToList();
        var host = inherit && hostId != Guid.Empty && hostId != serverId
            ? HostIssuersQuery(db, hostId).ToList()
            : [];
        return MergeIssuers(own, host);
    }

    public static async Task<List<TrustedAuthIssuer>> EffectiveTrustedIssuersAsync(
        this SneklDbContext db,
        CancellationToken ct = default)
    {
        if (!db.HasServerScope)
            return await db.TrustedAuthIssuers.AsNoTracking().Where(i => i.IsActive || i.IsRequired).ToListAsync(ct);

        var serverId = db.ScopedServerId;
        var hostId = await HostQuery(db).FirstOrDefaultAsync(ct);
        var inherit = await InheritsQuery(db, serverId, n => n.InheritHostAuthIssuers).FirstOrDefaultAsync(ct) ?? true;
        var own = await OwnIssuersQuery(db, serverId).ToListAsync(ct);
        var host = inherit && hostId != Guid.Empty && hostId != serverId
            ? await HostIssuersQuery(db, hostId).ToListAsync(ct)
            : [];
        return MergeIssuers(own, host);
    }

    /// <summary>Accepted hub URLs: the server's own (newest first), then the host's when inherited.</summary>
    public static async Task<List<string>> EffectiveAcceptedHubUrlsAsync(
        this SneklDbContext db,
        CancellationToken ct = default)
    {
        if (!db.HasServerScope)
        {
            var all = await AcceptedHubsQuery(db.HubInvites.AsNoTracking()).ToListAsync(ct);
            return NormalizeHubs(all);
        }

        var serverId = db.ScopedServerId;
        var own = await AcceptedHubsQuery(db.HubInvites.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.ServerId == serverId)).ToListAsync(ct);
        var hostId = await HostQuery(db).FirstOrDefaultAsync(ct);
        var inherit = await InheritsQuery(db, serverId, n => n.InheritHostHubs).FirstOrDefaultAsync(ct) ?? true;
        if (inherit && hostId != Guid.Empty && hostId != serverId)
        {
            own.AddRange(await AcceptedHubsQuery(db.HubInvites.IgnoreQueryFilters().AsNoTracking()
                .Where(i => i.ServerId == hostId)).ToListAsync(ct));
        }

        return NormalizeHubs(own);
    }

    /// <summary>
    /// Refuses to save a change that would drop or repoint a required issuer. Rows removed with
    /// ExecuteDelete (purging a whole server) do not pass through here.
    /// </summary>
    public static void GuardRequiredIssuers(ChangeTracker tracker)
    {
        foreach (var entry in tracker.Entries<TrustedAuthIssuer>())
        {
            if (entry.State is not (EntityState.Modified or EntityState.Deleted))
                continue;
            if (entry.OriginalValues[nameof(TrustedAuthIssuer.IsRequired)] is not true)
                continue;

            var url = entry.OriginalValues[nameof(TrustedAuthIssuer.IssuerUrl)] as string ?? entry.Entity.IssuerUrl;
            if (entry.State == EntityState.Deleted ||
                !entry.Entity.IsRequired ||
                !entry.Entity.IsActive ||
                !string.Equals(entry.Entity.IssuerUrl, url, StringComparison.Ordinal) ||
                !string.Equals(entry.Entity.JwksUrl, entry.OriginalValues[nameof(TrustedAuthIssuer.JwksUrl)] as string, StringComparison.Ordinal))
            {
                throw new RequiredAuthIssuerException(url);
            }
        }
    }

    private static IQueryable<Guid> HostQuery(SneklDbContext db) =>
        db.NodeConfig.AsNoTracking().OrderBy(n => n.CreatedAt).Select(n => n.Id);

    private static IQueryable<bool?> InheritsQuery(
        SneklDbContext db,
        Guid serverId,
        System.Linq.Expressions.Expression<Func<NodeConfig, bool>> flag) =>
        db.NodeConfig.AsNoTracking().Where(n => n.Id == serverId).Select(flag).Select(f => (bool?)f);

    private static IQueryable<TrustedAuthIssuer> OwnIssuersQuery(SneklDbContext db, Guid serverId) =>
        db.TrustedAuthIssuers.IgnoreQueryFilters().AsNoTracking().Where(i => i.ServerId == serverId);

    private static IQueryable<TrustedAuthIssuer> HostIssuersQuery(SneklDbContext db, Guid hostId) =>
        db.TrustedAuthIssuers.IgnoreQueryFilters().AsNoTracking().Where(i => i.ServerId == hostId && i.IsActive);

    private static IQueryable<string> AcceptedHubsQuery(IQueryable<HubInvite> invites) =>
        invites.Where(i => i.Status == "accepted").OrderByDescending(i => i.CreatedAt).Select(i => i.HubBaseUrl);

    private static List<TrustedAuthIssuer> MergeIssuers(List<TrustedAuthIssuer> own, List<TrustedAuthIssuer> host)
    {
        // Own rows decide for their URL: active or required rows count, an inactive own row
        // hides the host's issuer of the same URL on this server.
        var ownUrls = own.Select(i => Normalize(i.IssuerUrl)).ToHashSet(StringComparer.Ordinal);
        var result = own.Where(i => i.IsActive || i.IsRequired).ToList();
        var seen = new HashSet<string>(ownUrls, StringComparer.Ordinal);
        foreach (var issuer in host)
        {
            if (seen.Add(Normalize(issuer.IssuerUrl)))
                result.Add(issuer);
        }

        return result;
    }

    private static List<string> NormalizeHubs(IEnumerable<string> urls) =>
        urls.Select(u => u.Trim().TrimEnd('/'))
            .Where(u => u.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string Normalize(string url) => url.Trim().TrimEnd('/');
}

/// <summary>Raised when a change would remove the auth server a business-hosted server depends on.</summary>
public sealed class RequiredAuthIssuerException : InvalidOperationException
{
    public RequiredAuthIssuerException(string issuerUrl)
        : base($"The auth server {issuerUrl} is required by this server's business account, so it can't be removed, disabled or pointed at different keys.")
    {
        IssuerUrl = issuerUrl;
    }

    public string IssuerUrl { get; }
}
