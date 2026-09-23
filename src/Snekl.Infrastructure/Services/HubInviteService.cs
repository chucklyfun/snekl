using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IHubInviteService
{
    Task<HubInviteDto> RequestInviteAsync(Guid userId, CreateHubInviteRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<HubInviteDto>> ListPendingAsync(CancellationToken ct = default);
    Task AcceptAsync(Guid inviteId, CancellationToken ct = default);
    Task DeclineAsync(Guid inviteId, CancellationToken ct = default);
}

public class HubInviteService : IHubInviteService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly SneklDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly INodeService _node;
    private readonly IHttpClientFactory _http;

    public HubInviteService(
        SneklDbContext db,
        IPermissionService permissions,
        INodeService node,
        IHttpClientFactory http)
    {
        _db = db;
        _permissions = permissions;
        _node = node;
        _http = http;
    }

    public async Task<HubInviteDto> RequestInviteAsync(
        Guid userId,
        CreateHubInviteRequest request,
        CancellationToken ct = default)
    {
        if (!await _permissions.IsSuperUserAsync(userId, ct))
            await _permissions.RequireServerPermissionAsync(userId, ServerPermissions.RequestFederation, ct);

        var hubBaseUrl = request.HubBaseUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(hubBaseUrl))
            throw new InvalidOperationException("HubBaseUrl is required.");

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("User not found.");

        var pending = await _db.HubInvites.FirstOrDefaultAsync(
            i => i.HubBaseUrl == hubBaseUrl && i.Status == "pending", ct);
        if (pending is not null)
            return ToDto(pending);

        var invite = new HubInvite
        {
            HubBaseUrl = hubBaseUrl,
            HubName = string.IsNullOrWhiteSpace(request.HubName) ? hubBaseUrl : request.HubName.Trim(),
            RequestedByDid = user.Did ?? string.Empty,
            RequestedByUsername = user.Username,
            Status = "pending"
        };
        _db.HubInvites.Add(invite);
        await _db.SaveChangesAsync(ct);
        return ToDto(invite);
    }

    public async Task<IReadOnlyList<HubInviteDto>> ListPendingAsync(CancellationToken ct = default)
    {
        var rows = await _db.HubInvites
            .AsNoTracking()
            .Where(i => i.Status == "pending")
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task AcceptAsync(Guid inviteId, CancellationToken ct = default)
    {
        var invite = await _db.HubInvites.FirstOrDefaultAsync(i => i.Id == inviteId, ct)
            ?? throw new InvalidOperationException("Invite not found.");
        if (invite.Status != "pending")
            throw new InvalidOperationException("Invite is no longer pending.");

        var node = await _node.GetOrCreateAsync(ct);
        var client = _http.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"{invite.HubBaseUrl.TrimEnd('/')}/api/hub/join-requests/accept",
            new
            {
                serverDid = node.Did,
                acceptedBy = "origin",
                originInviteId = invite.Id
            },
            ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(body) ? "Hub rejected the accept." : body);
        }

        var map = await client.GetFromJsonAsync<HubMapAck>(
            $"{invite.HubBaseUrl.TrimEnd('/')}/api/hub/map", JsonOptions, ct);
        if (map is not null)
        {
            await client.PostAsJsonAsync(
                $"{invite.HubBaseUrl.TrimEnd('/')}/api/hub/map/ack",
                new { serverDid = node.Did, mapVersion = map.MapVersion },
                ct);
        }

        invite.Status = "accepted";
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeclineAsync(Guid inviteId, CancellationToken ct = default)
    {
        var invite = await _db.HubInvites.FirstOrDefaultAsync(i => i.Id == inviteId, ct)
            ?? throw new InvalidOperationException("Invite not found.");
        if (invite.Status != "pending")
            throw new InvalidOperationException("Invite is no longer pending.");

        var node = await _node.GetOrCreateAsync(ct);
        var client = _http.CreateClient();
        await client.PostAsJsonAsync(
            $"{invite.HubBaseUrl.TrimEnd('/')}/api/hub/join-requests/decline",
            new { serverDid = node.Did },
            ct);

        invite.Status = "declined";
        await _db.SaveChangesAsync(ct);
    }

    private static HubInviteDto ToDto(HubInvite invite) =>
        new(invite.Id, invite.HubBaseUrl, invite.HubName, invite.RequestedByDid,
            invite.RequestedByUsername, invite.Status, invite.CreatedAt);

    private sealed record HubMapAck(long MapVersion);
}
