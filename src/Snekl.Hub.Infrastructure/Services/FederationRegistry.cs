using Microsoft.EntityFrameworkCore;
using Snekl.Hub.Domain;
using Snekl.Hub.Domain.Dtos;
using Snekl.Hub.Domain.Entities;
using Snekl.Hub.Infrastructure.Data;

namespace Snekl.Hub.Infrastructure.Services;

public interface IFederationRegistry
{
    Task<long> GetMapVersionAsync(CancellationToken ct = default);
    Task<FederationMapDto> GetMapAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RegisteredServerDto>> ListServersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RegisteredServerDto>> ListJoinRequestsAsync(string? serverDid = null, CancellationToken ct = default);
    Task<RegisteredServerDto> RegisterServerAsync(RegisterServerRequest request, CancellationToken ct = default);
    Task<RegisteredServerDto> AcceptJoinAsync(AcceptJoinRequest request, CancellationToken ct = default);
    Task DeclineJoinAsync(DeclineJoinRequest request, CancellationToken ct = default);
    Task AckMapAsync(MapAckRequest request, CancellationToken ct = default);
    Task UnregisterServerAsync(string serverDid, CancellationToken ct = default);
}

public class FederationRegistry : IFederationRegistry
{
    private readonly HubDbContext _db;

    public FederationRegistry(HubDbContext db) => _db = db;

    public async Task<long> GetMapVersionAsync(CancellationToken ct = default)
    {
        var state = await EnsureStateAsync(ct);
        return state.MapVersion;
    }

    public async Task<FederationMapDto> GetMapAsync(CancellationToken ct = default)
    {
        var state = await EnsureStateAsync(ct);
        var servers = await ListServersAsync(ct);
        return new FederationMapDto(state.MapVersion, servers);
    }

    public async Task<IReadOnlyList<RegisteredServerDto>> ListServersAsync(CancellationToken ct = default)
    {
        var rows = await _db.RegisteredServers
            .AsNoTracking()
            .Where(s => s.Status == FederationMembership.StatusActive)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<RegisteredServerDto>> ListJoinRequestsAsync(
        string? serverDid = null,
        CancellationToken ct = default)
    {
        var query = _db.RegisteredServers
            .AsNoTracking()
            .Where(s => s.Status == FederationMembership.StatusPending);

        if (!string.IsNullOrWhiteSpace(serverDid))
        {
            var keys = DidLookupKeys(serverDid);
            query = query.Where(s => keys.Contains(s.ServerDid));
        }

        var rows = await query.OrderBy(s => s.Name).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<RegisteredServerDto> RegisterServerAsync(RegisterServerRequest request, CancellationToken ct = default)
    {
        var serverDid = request.ServerDid.Trim();
        var baseUrl = request.BaseUrl.Trim().TrimEnd('/');
        var name = string.IsNullOrWhiteSpace(request.Name) ? baseUrl : request.Name.Trim();
        var initiatedBy = FederationMembership.NormalizeInitiator(request.InitiatedBy);

        if (string.IsNullOrWhiteSpace(serverDid))
            throw new InvalidOperationException("ServerDid is required.");
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("BaseUrl is required.");

        var byDid = await _db.RegisteredServers.FirstOrDefaultAsync(s => s.ServerDid == serverDid, ct);
        var byUrl = await _db.RegisteredServers.FirstOrDefaultAsync(s => s.BaseUrl == baseUrl, ct);
        if (byDid is not null && byUrl is not null && byDid.Id != byUrl.Id)
            throw new InvalidOperationException(
                "ServerDid and BaseUrl belong to two different registered servers.");

        var existing = byDid ?? byUrl;
        var state = await EnsureStateAsync(ct);

        if (existing is not null)
        {
            existing.ServerDid = serverDid;
            existing.BaseUrl = baseUrl;
            existing.Name = name;
            existing.LastHeartbeatAt = DateTime.UtcNow;
            if (request.OriginInviteId.HasValue)
                existing.OriginInviteId = request.OriginInviteId;

            if (existing.Status == FederationMembership.StatusActive)
            {
                state.MapVersion += 1;
                state.UpdatedAt = DateTime.UtcNow;
            }
            else if (existing.InitiatedBy != initiatedBy)
            {
                Activate(existing, state);
            }
            else
            {
                existing.InitiatedBy = initiatedBy;
            }
        }
        else
        {
            existing = new RegisteredServer
            {
                ServerDid = serverDid,
                BaseUrl = baseUrl,
                Name = name,
                LastHeartbeatAt = DateTime.UtcNow,
                AckedMapVersion = 0,
                Status = FederationMembership.StatusPending,
                InitiatedBy = initiatedBy,
                OriginInviteId = request.OriginInviteId
            };
            _db.RegisteredServers.Add(existing);
        }

        await _db.SaveChangesAsync(ct);
        return ToDto(existing);
    }

    public async Task<RegisteredServerDto> AcceptJoinAsync(AcceptJoinRequest request, CancellationToken ct = default)
    {
        var acceptedBy = FederationMembership.NormalizeInitiator(request.AcceptedBy);
        var server = await FindServerAsync(request.ServerDid, ct)
            ?? throw new InvalidOperationException("No pending federation request for that server.");

        if (server.Status == FederationMembership.StatusActive)
            return ToDto(server);

        if (server.InitiatedBy == acceptedBy)
            throw new InvalidOperationException("The other party must accept this request.");

        if (acceptedBy == FederationMembership.InitiatorOrigin
            && server.OriginInviteId.HasValue
            && server.OriginInviteId != request.OriginInviteId)
            throw new InvalidOperationException("Origin invite confirmation is required.");

        var state = await EnsureStateAsync(ct);
        Activate(server, state);
        await _db.SaveChangesAsync(ct);
        return ToDto(server);
    }

    public async Task DeclineJoinAsync(DeclineJoinRequest request, CancellationToken ct = default)
    {
        var server = await FindServerAsync(request.ServerDid, ct)
            ?? throw new InvalidOperationException("No pending federation request for that server.");

        if (server.Status == FederationMembership.StatusActive)
            throw new InvalidOperationException("Use unregister to remove an active federation member.");

        _db.RegisteredServers.Remove(server);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AckMapAsync(MapAckRequest request, CancellationToken ct = default)
    {
        var server = await FindServerAsync(request.ServerDid, ct)
            ?? throw new InvalidOperationException("Server is not registered.");
        if (server.Status != FederationMembership.StatusActive)
            throw new InvalidOperationException("Server is not an active federation member.");

        server.AckedMapVersion = request.MapVersion;
        server.LastHeartbeatAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task UnregisterServerAsync(string serverDid, CancellationToken ct = default)
    {
        var server = await FindServerAsync(serverDid, ct)
            ?? throw new InvalidOperationException("Server is not registered.");

        var wasActive = server.Status == FederationMembership.StatusActive;
        _db.RegisteredServers.Remove(server);
        if (wasActive)
        {
            var state = await EnsureStateAsync(ct);
            state.MapVersion += 1;
            state.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// did:web host:port is stored percent-encoded (%3A). URL path/query decoding can
    /// produce either form, so match both.
    /// </summary>
    internal static string[] DidLookupKeys(string serverDid)
    {
        var trimmed = serverDid.Trim();
        var keys = new HashSet<string>(StringComparer.Ordinal) { trimmed };
        try
        {
            var unescaped = Uri.UnescapeDataString(trimmed);
            keys.Add(unescaped);
            if (unescaped.StartsWith("did:web:", StringComparison.OrdinalIgnoreCase))
            {
                var methodSpecific = unescaped["did:web:".Length..];
                keys.Add("did:web:" + Uri.EscapeDataString(Uri.UnescapeDataString(methodSpecific)));
            }
        }
        catch (UriFormatException)
        {
            // keep the trimmed value only
        }

        return keys.ToArray();
    }

    private async Task<RegisteredServer?> FindServerAsync(string serverDid, CancellationToken ct)
    {
        var keys = DidLookupKeys(serverDid);
        return await _db.RegisteredServers.FirstOrDefaultAsync(s => keys.Contains(s.ServerDid), ct);
    }

    private static void Activate(RegisteredServer server, FederationState state)
    {
        server.Status = FederationMembership.StatusActive;
        server.LastHeartbeatAt = DateTime.UtcNow;
        state.MapVersion += 1;
        state.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<FederationState> EnsureStateAsync(CancellationToken ct)
    {
        var existing = await _db.FederationState.FirstOrDefaultAsync(ct);
        if (existing is not null) return existing;

        try
        {
            var created = new FederationState();
            _db.FederationState.Add(created);
            await _db.SaveChangesAsync(ct);
            return created;
        }
        catch (DbUpdateException)
        {
            // Concurrent first request already inserted the singleton row.
            _db.ChangeTracker.Clear();
            return await _db.FederationState.FirstAsync(ct);
        }
    }

    private static RegisteredServerDto ToDto(RegisteredServer s) =>
        new(s.ServerDid, s.BaseUrl, s.Name, s.RegisteredAt, s.AckedMapVersion, s.Status, s.InitiatedBy, s.OriginInviteId);
}
