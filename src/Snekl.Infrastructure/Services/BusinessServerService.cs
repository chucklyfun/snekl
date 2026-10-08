using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public enum BusinessServerOutcome
{
    /// <summary>A new server was spawned and linked.</summary>
    Created,
    /// <summary>A server spawned earlier for this business (link had failed) was linked now. No new server.</summary>
    Relinked,
    NotSignedIn,
    NoBusiness,
    NotApproved,
    AlreadyHasServer,
    AuthUnavailable,
    AuthRejected,
    /// <summary>The server exists but Auth did not record the link. Retrying links it without a new spawn.</summary>
    LinkFailed
}

public sealed record BusinessServerResult(
    BusinessServerOutcome Outcome,
    string? Error = null,
    string? BusinessStatus = null,
    Guid? BusinessAccountId = null,
    VirtualServerSummary? Server = null,
    Guid? LinkedServerId = null,
    string? LinkedServerDid = null);

/// <summary>
/// Owner-created hosted server: checks with Auth (signed) that the member's business is approved and
/// has no server, spawns one with the same <see cref="IVirtualServerService.SpawnAsync"/> operators use,
/// then reports it back to Auth (signed). If the report fails, the next attempt finds the server this
/// host already spawned for the owner (same owner DID and business name) and links it instead of
/// spawning a second one.
/// </summary>
public interface IBusinessServerService
{
    Task<BusinessServerResult> CreateAsync(Guid userId, string memberDid, Guid? businessAccountId, CancellationToken ct = default);
}

public sealed class BusinessServerService : IBusinessServerService
{
    // One create at a time per member on this host, so a double click cannot spawn two servers.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

    private readonly SneklDbContext _db;
    private readonly IBusinessHostingAuthClient _auth;
    private readonly IVirtualServerService _servers;

    public BusinessServerService(SneklDbContext db, IBusinessHostingAuthClient auth, IVirtualServerService servers)
    {
        _db = db;
        _auth = auth;
        _servers = servers;
    }

    public async Task<BusinessServerResult> CreateAsync(
        Guid userId,
        string memberDid,
        Guid? businessAccountId,
        CancellationToken ct = default)
    {
        var did = (memberDid ?? string.Empty).Trim();
        if (did.Length == 0)
            return new BusinessServerResult(BusinessServerOutcome.NotSignedIn, "Sign in with a Snekl Account to create a hosted server.");

        // userId comes from the claims transformation, not the client. Shadow users made before
        // sign-in was server-aware can carry an empty ServerId, so look past the server filter.
        var issuer = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.AuthIssuerUrl)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(issuer))
            return new BusinessServerResult(BusinessServerOutcome.NotSignedIn, "Sign in with a Snekl Account to create a hosted server.");

        var gate = Locks.GetOrAdd($"{issuer.Trim().TrimEnd('/')}|{did}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await CreateLockedAsync(issuer, did, businessAccountId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<BusinessServerResult> CreateLockedAsync(
        string issuer,
        string did,
        Guid? businessAccountId,
        CancellationToken ct)
    {
        var statusCall = await _auth.GetStatusAsync(issuer, did, businessAccountId, ct);
        if (statusCall.StatusCode == 404)
            return new BusinessServerResult(BusinessServerOutcome.NoBusiness, "No business account for this Snekl Account.");
        if (!statusCall.IsSuccess)
            return AuthFailure(statusCall.StatusCode, statusCall.Error);

        var business = statusCall.Value!;
        if (business.ServerId is Guid linkedId)
            return new BusinessServerResult(
                BusinessServerOutcome.AlreadyHasServer,
                "This business already has a hosted server.",
                business.Status,
                business.BusinessAccountId,
                LinkedServerId: linkedId,
                LinkedServerDid: business.ServerDid);

        if (!string.Equals(business.Status, BusinessHostingStatusNames.Approved, StringComparison.Ordinal))
            return new BusinessServerResult(
                BusinessServerOutcome.NotApproved,
                business.Status == BusinessHostingStatusNames.Rejected
                    ? "This business account was not approved."
                    : "This business account is waiting for approval.",
                business.Status,
                business.BusinessAccountId);

        var name = business.BusinessName.Trim();
        var server = await FindUnlinkedServerAsync(did, name, ct);
        var created = false;
        if (server is null)
        {
            var username = string.IsNullOrWhiteSpace(business.Username) ? did : business.Username;
            // The business page and account live on this auth server, so the new server must
            // always trust it, whatever later happens to the host's list.
            server = await _servers.SpawnAsync(new SpawnServerRequest(name, username, did, issuer), ct);
            created = true;
        }
        else
        {
            // A leftover from an earlier attempt: make sure it still requires that auth server.
            await _servers.PinAuthIssuerAsync(server.Id, issuer, ct);
        }

        var linkCall = await _auth.LinkAsync(
            issuer,
            new BusinessServerLink(did, business.BusinessAccountId, server.Id, server.Did, server.BaseUrl),
            ct);
        if (!linkCall.IsSuccess)
            return new BusinessServerResult(
                BusinessServerOutcome.LinkFailed,
                $"The server was created, but the auth server did not record it yet: {linkCall.Error ?? "unknown error"} Try again to finish.",
                business.Status,
                business.BusinessAccountId,
                server);

        return new BusinessServerResult(
            created ? BusinessServerOutcome.Created : BusinessServerOutcome.Relinked,
            null,
            linkCall.Value!.Business.Status,
            business.BusinessAccountId,
            server);
    }

    /// <summary>
    /// A server this host spawned for the owner (owner user carries the member DID inside that server)
    /// with the business name, not disabled, and not the host's own first server. Used to recover
    /// when spawning succeeded but the link to Auth did not.
    /// </summary>
    private async Task<VirtualServerSummary?> FindUnlinkedServerAsync(string did, string name, CancellationToken ct)
    {
        var hostId = await _db.NodeConfig.AsNoTracking()
            .OrderBy(n => n.CreatedAt)
            .Select(n => (Guid?)n.Id)
            .FirstOrDefaultAsync(ct);

        var match = await (
                from n in _db.NodeConfig.AsNoTracking()
                join u in _db.Users.IgnoreQueryFilters().AsNoTracking() on n.OwnerUserId equals u.Id
                where n.Id != hostId
                      && n.DisabledAt == null
                      && n.Name == name
                      && u.ServerId == n.Id
                      && u.Did == did
                orderby n.CreatedAt
                select new { Node = n, u.Username })
            .FirstOrDefaultAsync(ct);

        return match is null
            ? null
            : new VirtualServerSummary(
                match.Node.Id,
                match.Node.Name,
                match.Node.Did,
                match.Node.BaseUrl,
                match.Username,
                match.Node.OwnerUserId,
                match.Node.DisabledAt,
                match.Node.CreatedAt);
    }

    private static BusinessServerResult AuthFailure(int statusCode, string? error) =>
        statusCode == 0
            ? new BusinessServerResult(BusinessServerOutcome.AuthUnavailable, error ?? "Could not reach the auth server.")
            : new BusinessServerResult(BusinessServerOutcome.AuthRejected, error ?? $"Auth server returned HTTP {statusCode}.");
}
