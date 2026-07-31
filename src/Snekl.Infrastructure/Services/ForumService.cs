using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IForumService
{
    Task<ForumDto> CreateAsync(Guid userId, CreateForumRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ForumDto>> ListAsync(CancellationToken ct = default);
    Task<ForumDto?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<Forum?> GetEntityBySlugAsync(string slug, CancellationToken ct = default);
    Task UpsertFederatedAsync(FederatedForumPayload payload, Guid sourceNodeId, string sourceNodeUrl, CancellationToken ct = default);
}

public class ForumService : IForumService
{
    private readonly SneklDbContext _db;

    public ForumService(SneklDbContext db) => _db = db;

    public async Task<ForumDto> CreateAsync(Guid userId, CreateForumRequest request, CancellationToken ct = default)
    {
        var slug = request.Slug.Trim().ToLowerInvariant();
        if (await _db.Forums.AnyAsync(f => f.Slug == slug, ct))
            throw new InvalidOperationException("Forum slug already exists.");

        var forum = new Forum
        {
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description.Trim(),
            CreatedByUserId = userId
        };

        _db.Forums.Add(forum);
        await _db.SaveChangesAsync(ct);

        var createdBy = await _db.Users.FindAsync([userId], ct);
        return ToDto(forum, createdBy, 0);
    }

    public async Task<IReadOnlyList<ForumDto>> ListAsync(CancellationToken ct = default)
    {
        var forums = await _db.Forums
            .Include(f => f.CreatedBy)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);

        var postCounts = await _db.Posts
            .Where(p => p.ParentPostId == null)
            .GroupBy(p => p.ForumId)
            .Select(g => new { ForumId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ForumId, x => x.Count, ct);

        return forums.Select(f => ToDto(f, f.CreatedBy, postCounts.GetValueOrDefault(f.Id))).ToList();
    }

    public async Task<ForumDto?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var forum = await GetEntityBySlugAsync(slug, ct);
        if (forum is null) return null;

        var postCount = await _db.Posts.CountAsync(p => p.ForumId == forum.Id && p.ParentPostId == null, ct);
        return ToDto(forum, forum.CreatedBy, postCount);
    }

    public Task<Forum?> GetEntityBySlugAsync(string slug, CancellationToken ct = default) =>
        _db.Forums.Include(f => f.CreatedBy).FirstOrDefaultAsync(f => f.Slug == slug.ToLowerInvariant(), ct);

    public async Task UpsertFederatedAsync(FederatedForumPayload payload, Guid sourceNodeId, string sourceNodeUrl, CancellationToken ct = default)
    {
        var existing = await _db.Forums.FirstOrDefaultAsync(f => f.Id == payload.Id, ct);
        if (existing is not null) return;

        var author = await _db.Users.FirstOrDefaultAsync(u => u.Id == payload.CreatedBy.Id, ct);
        if (author is null)
        {
            author = new User
            {
                Id = payload.CreatedBy.Id,
                Username = payload.CreatedBy.Username,
                DisplayName = payload.CreatedBy.DisplayName,
                Bio = payload.CreatedBy.Bio,
                OriginNodeId = sourceNodeId,
                OriginNodeUrl = payload.CreatedBy.OriginNodeUrl
            };
            _db.Users.Add(author);
        }

        var forum = new Forum
        {
            Id = payload.Id,
            Name = payload.Name,
            Slug = payload.Slug,
            Description = payload.Description,
            CreatedByUserId = author.Id,
            CreatedAt = payload.CreatedAt,
            UpdatedAt = payload.CreatedAt,
            OriginNodeId = sourceNodeId,
            OriginNodeUrl = sourceNodeUrl
        };

        _db.Forums.Add(forum);
        await _db.SaveChangesAsync(ct);
    }

    public static ForumDto ToDto(Forum forum, User? createdBy, int postCount) => new(
        forum.Id,
        forum.Name,
        forum.Slug,
        forum.Description,
        createdBy is null ? null : AuthService.ToDto(createdBy),
        forum.OriginNodeUrl,
        forum.IsFederated,
        postCount,
        forum.CreatedAt);
}
