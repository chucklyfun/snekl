using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IPostService
{
    Task<PostDto> CreateAsync(Guid userId, CreatePostRequest request, CancellationToken ct = default);
    Task<Post?> GetEntityAsync(Guid postId, CancellationToken ct = default);
    Task<IReadOnlyList<PostDto>> GetForumThreadsAsync(Guid forumId, int page, int pageSize, CancellationToken ct = default);
    Task<PostDto?> GetThreadAsync(Guid postId, CancellationToken ct = default);
    Task UpsertFederatedAsync(FederatedPostPayload payload, Guid sourceNodeId, string sourceNodeUrl, CancellationToken ct = default);
}

public class PostService : IPostService
{
    private readonly SneklDbContext _db;

    public PostService(SneklDbContext db) => _db = db;

    public async Task<PostDto> CreateAsync(Guid userId, CreatePostRequest request, CancellationToken ct = default)
    {
        var forum = await _db.Forums.FindAsync([request.ForumId], ct)
            ?? throw new InvalidOperationException("Forum not found.");

        if (request.ParentPostId.HasValue)
        {
            var parent = await _db.Posts.FindAsync([request.ParentPostId.Value], ct)
                ?? throw new InvalidOperationException("Parent post not found.");
            parent.ReplyCount++;
        }

        var post = new Post
        {
            ForumId = forum.Id,
            ParentPostId = request.ParentPostId,
            AuthorUserId = userId,
            Message = request.Message.Trim()
        };

        _db.Posts.Add(post);
        await _db.SaveChangesAsync(ct);

        var author = await _db.Users.FindAsync([userId], ct);
        return ToDto(post, author!, []);
    }

    public Task<Post?> GetEntityAsync(Guid postId, CancellationToken ct = default) =>
        _db.Posts.Include(p => p.Author).Include(p => p.Forum).FirstOrDefaultAsync(p => p.Id == postId, ct);

    public async Task<IReadOnlyList<PostDto>> GetForumThreadsAsync(Guid forumId, int page, int pageSize, CancellationToken ct = default)
    {
        var posts = await _db.Posts
            .Include(p => p.Author)
            .Where(p => p.ForumId == forumId && p.ParentPostId == null)
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return posts.Select(p => ToDto(p, p.Author!, [])).ToList();
    }

    public async Task<PostDto?> GetThreadAsync(Guid postId, CancellationToken ct = default)
    {
        var root = await _db.Posts
            .Include(p => p.Author)
            .FirstOrDefaultAsync(p => p.Id == postId, ct);

        if (root is null) return null;

        var allPosts = await _db.Posts
            .Include(p => p.Author)
            .Where(p => p.ForumId == root.ForumId)
            .ToListAsync(ct);

        return BuildTree(root, allPosts);
    }

    public async Task UpsertFederatedAsync(FederatedPostPayload payload, Guid sourceNodeId, string sourceNodeUrl, CancellationToken ct = default)
    {
        if (await _db.Posts.AnyAsync(p => p.Id == payload.Id, ct)) return;

        var forum = await _db.Forums.FirstOrDefaultAsync(f => f.Id == payload.ForumId, ct);
        if (forum is null)
        {
            await new ForumService(_db).UpsertFederatedAsync(
                new FederatedForumPayload(
                    payload.ForumId,
                    payload.ForumName,
                    payload.ForumSlug,
                    string.Empty,
                    payload.Author,
                    payload.CreatedAt),
                sourceNodeId,
                sourceNodeUrl,
                ct);
        }

        var author = await _db.Users.FirstOrDefaultAsync(u => u.Id == payload.Author.Id, ct);
        if (author is null)
        {
            author = new User
            {
                Id = payload.Author.Id,
                Username = payload.Author.Username,
                DisplayName = payload.Author.DisplayName,
                Bio = payload.Author.Bio,
                OriginNodeId = sourceNodeId,
                OriginNodeUrl = payload.Author.OriginNodeUrl
            };
            _db.Users.Add(author);
        }

        var post = new Post
        {
            Id = payload.Id,
            ForumId = payload.ForumId,
            ParentPostId = payload.ParentPostId,
            AuthorUserId = author.Id,
            Message = payload.Message,
            CreatedAt = payload.CreatedAt,
            UpdatedAt = payload.CreatedAt,
            OriginNodeId = sourceNodeId,
            OriginNodeUrl = sourceNodeUrl
        };

        if (payload.ParentPostId.HasValue)
        {
            var parent = await _db.Posts.FindAsync([payload.ParentPostId.Value], ct);
            if (parent is not null) parent.ReplyCount++;
        }

        _db.Posts.Add(post);
        await _db.SaveChangesAsync(ct);
    }

    private static PostDto BuildTree(Post root, List<Post> allPosts)
    {
        var lookup = allPosts
            .GroupBy(p => p.ParentPostId ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => g.ToList());
        return BuildNode(root, lookup);
    }

    private static PostDto BuildNode(Post post, Dictionary<Guid, List<Post>> lookup)
    {
        var children = lookup.GetValueOrDefault(post.Id, [])
            .OrderBy(p => p.CreatedAt)
            .Select(p => BuildNode(p, lookup))
            .ToList();

        return ToDto(post, post.Author!, children);
    }

    public static PostDto ToDto(Post post, User author, List<PostDto> replies) => new(
        post.Id,
        post.ForumId,
        post.ParentPostId,
        AuthService.ToDto(author),
        post.Message,
        post.ReplyCount,
        post.OriginNodeUrl,
        post.IsFederated,
        post.CreatedAt,
        replies);
}
