using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IOwnershipService
{
    Task<NodeInfoDto> TransferServerOwnershipAsync(Guid actorId, string username, CancellationToken ct = default);
    Task TransferForumOwnershipAsync(Guid forumId, Guid actorId, string username, CancellationToken ct = default);
}

public class OwnershipService : IOwnershipService
{
    private readonly SneklDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly IRoleSeedService _roleSeed;
    private readonly INodeService _nodes;

    public OwnershipService(
        SneklDbContext db,
        IPermissionService permissions,
        IRoleSeedService roleSeed,
        INodeService nodes)
    {
        _db = db;
        _permissions = permissions;
        _roleSeed = roleSeed;
        _nodes = nodes;
    }

    public async Task<NodeInfoDto> TransferServerOwnershipAsync(
        Guid actorId,
        string username,
        CancellationToken ct = default)
    {
        if (!await _permissions.IsServerOwnerAsync(actorId, ct))
            throw new InvalidOperationException("Only the server owner can transfer server ownership.");

        var recipient = await OwnershipLookup.RequireLocalUserAsync(_db, username, ct);
        if (recipient.Id == actorId)
            throw new InvalidOperationException("You already own this server.");

        var node = await _db.NodeConfig.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Node is not configured.");
        node.OwnerUserId = recipient.Id;
        await _db.SaveChangesAsync(ct);
        return await _nodes.GetInfoAsync(ct);
    }

    public async Task TransferForumOwnershipAsync(
        Guid forumId,
        Guid actorId,
        string username,
        CancellationToken ct = default)
    {
        var forum = await _db.Forums.FirstOrDefaultAsync(f => f.Id == forumId, ct)
            ?? throw new InvalidOperationException("Nest not found.");

        var isOwner = forum.OwnerUserId == actorId;
        var canOverride = await _permissions.CanOverrideForumOwnerAsync(actorId, ct);
        if (!isOwner && !canOverride)
            throw new InvalidOperationException("Only the nest owner or a server-level admin can transfer nest ownership.");

        var recipient = await OwnershipLookup.RequireLocalUserAsync(_db, username, ct);
        if (forum.OwnerUserId == recipient.Id)
            throw new InvalidOperationException("That user already owns this nest.");

        forum.OwnerUserId = recipient.Id;
        await _roleSeed.EnsureForumOwnerAsync(forum.Id, recipient.Id, ct);
        await _db.SaveChangesAsync(ct);
    }
}
