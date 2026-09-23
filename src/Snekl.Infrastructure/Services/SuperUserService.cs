using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface ISuperUserService
{
    Task<IReadOnlyList<SuperUserDto>> ListAsync(CancellationToken ct = default);
    Task<SuperUserDto> GrantAsync(Guid siteAdminId, string username, string? note, CancellationToken ct = default);
    Task RevokeAsync(Guid userId, CancellationToken ct = default);
}

public class SuperUserService : ISuperUserService
{
    private readonly SneklDbContext _db;
    private readonly IPermissionService _permissions;

    public SuperUserService(SneklDbContext db, IPermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<SuperUserDto>> ListAsync(CancellationToken ct = default)
    {
        var grants = await _db.SuperUserGrants
            .Include(g => g.User)
            .OrderByDescending(g => g.GrantedAt)
            .ToListAsync(ct);

        return grants.Select(g => new SuperUserDto(
            g.UserId,
            g.User?.Username ?? "unknown",
            g.User?.DisplayName ?? "Unknown",
            g.GrantedBySiteAdminId,
            g.GrantedAt,
            g.Note)).ToList();
    }

    public async Task<SuperUserDto> GrantAsync(Guid siteAdminId, string username, string? note, CancellationToken ct = default)
    {
        var normalized = username.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == normalized, ct)
            ?? throw new InvalidOperationException("User not found.");

        if (await _db.SuperUserGrants.AnyAsync(g => g.UserId == user.Id, ct))
            throw new InvalidOperationException("User is already a Super User.");

        var grant = new SuperUserGrant
        {
            UserId = user.Id,
            GrantedBySiteAdminId = siteAdminId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };

        _db.SuperUserGrants.Add(grant);
        await _db.SaveChangesAsync(ct);

        return new SuperUserDto(user.Id, user.Username, user.DisplayName, siteAdminId, grant.GrantedAt, grant.Note);
    }

    public async Task RevokeAsync(Guid userId, CancellationToken ct = default)
    {
        if (await _permissions.IsServerOwnerAsync(userId, ct))
            throw new InvalidOperationException("Cannot revoke Super User from the server owner.");

        var grant = await _db.SuperUserGrants.FirstOrDefaultAsync(g => g.UserId == userId, ct)
            ?? throw new InvalidOperationException("Super User grant not found.");

        _db.SuperUserGrants.Remove(grant);
        await _db.SaveChangesAsync(ct);
    }
}
