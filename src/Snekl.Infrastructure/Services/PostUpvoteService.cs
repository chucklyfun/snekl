using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IPostUpvoteService
{
    Task<UpvoteResultDto> ToggleAsync(Guid postId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlySet<Guid>> GetViewerUpvotedPostIdsAsync(IEnumerable<Guid> postIds, Guid? viewerUserId, CancellationToken ct = default);
}

public class PostUpvoteService : IPostUpvoteService
{
    private readonly SneklDbContext _db;

    public PostUpvoteService(SneklDbContext db) => _db = db;

    public async Task<UpvoteResultDto> ToggleAsync(Guid postId, Guid userId, CancellationToken ct = default)
    {
        var post = await _db.Posts.FindAsync([postId], ct)
            ?? throw new InvalidOperationException("Post not found.");

        var existing = await _db.PostUpvotes
            .FirstOrDefaultAsync(u => u.PostId == postId && u.UserId == userId, ct);

        if (existing is not null)
        {
            _db.PostUpvotes.Remove(existing);
            post.UpvoteCount = Math.Max(0, post.UpvoteCount - 1);
            await _db.SaveChangesAsync(ct);
            return new UpvoteResultDto(post.UpvoteCount, false);
        }

        _db.PostUpvotes.Add(new Domain.Entities.PostUpvote
        {
            PostId = postId,
            UserId = userId
        });
        post.UpvoteCount++;
        await _db.SaveChangesAsync(ct);
        return new UpvoteResultDto(post.UpvoteCount, true);
    }

    public async Task<IReadOnlySet<Guid>> GetViewerUpvotedPostIdsAsync(
        IEnumerable<Guid> postIds,
        Guid? viewerUserId,
        CancellationToken ct = default)
    {
        if (viewerUserId is null) return new HashSet<Guid>();

        var ids = postIds.Distinct().ToList();
        if (ids.Count == 0) return new HashSet<Guid>();

        var upvoted = await _db.PostUpvotes
            .Where(u => u.UserId == viewerUserId.Value && ids.Contains(u.PostId))
            .Select(u => u.PostId)
            .ToListAsync(ct);

        return upvoted.ToHashSet();
    }
}
