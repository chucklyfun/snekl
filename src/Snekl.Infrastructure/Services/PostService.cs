using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IPostService
{
    Task<PostDto> CreateAsync(Guid userId, CreatePostRequest request, Guid? viewerUserId = null, CancellationToken ct = default);
    Task<Post?> GetEntityAsync(Guid postId, CancellationToken ct = default);
    Task<IReadOnlyList<PostDto>> GetForumThreadsAsync(Guid forumId, int page, int pageSize, Guid? viewerUserId = null, CancellationToken ct = default);
    Task<PostDto?> GetThreadAsync(Guid postId, Guid? viewerUserId = null, CancellationToken ct = default);
    Task UpsertFederatedAsync(FederatedPostPayload payload, Guid sourceNodeId, string sourceNodeUrl, CancellationToken ct = default);
}

public class PostService : IPostService
{
    private readonly SneklDbContext _db;
    private readonly IUserPresentationService _presentation;
    private readonly IPermissionService _permissions;
    private readonly IForumService _forums;

    public PostService(
        SneklDbContext db,
        IUserPresentationService presentation,
        IPermissionService permissions,
        IForumService forums)
    {
        _db = db;
        _presentation = presentation;
        _permissions = permissions;
        _forums = forums;
    }

    public async Task<PostDto> CreateAsync(Guid userId, CreatePostRequest request, Guid? viewerUserId = null, CancellationToken ct = default)
    {
        var forum = await _db.Forums.FindAsync([request.ForumId], ct)
            ?? throw new InvalidOperationException("Forum not found.");

        if (request.ParentPostId.HasValue)
            await _permissions.RequireForumPermissionAsync(userId, forum.Id, ForumPermissions.Reply, ct);
        else
            await _permissions.RequireForumPermissionAsync(userId, forum.Id, ForumPermissions.CreateThread, ct);

        if (request.ParentPostId.HasValue)
        {
            var parent = await _db.Posts.FindAsync([request.ParentPostId.Value], ct)
                ?? throw new InvalidOperationException("Parent post not found.");

            if (parent.ForumId != forum.Id)
                throw new InvalidOperationException("Parent post belongs to a different forum.");

            await IncrementReplyCountsAsync(parent.Id, ct);
        }

        var author = await _db.Users.FindAsync([userId], ct);
        var post = new Post
        {
            ForumId = forum.Id,
            ParentPostId = request.ParentPostId,
            AuthorUserId = userId,
            AuthorPersonClusterId = null,
            AuthorDid = author?.Did,
            Message = request.Message.Trim()
        };

        _db.Posts.Add(post);
        await _db.SaveChangesAsync(ct);

        var authorPresentation = await _presentation.GetPresentationAsync(userId, viewerUserId ?? userId, forum.Id, ct);
        return ToDto(post, authorPresentation, []);
    }

    public Task<Post?> GetEntityAsync(Guid postId, CancellationToken ct = default) =>
        _db.Posts.Include(p => p.Author).Include(p => p.Forum).FirstOrDefaultAsync(p => p.Id == postId, ct);

    public async Task<IReadOnlyList<PostDto>> GetForumThreadsAsync(Guid forumId, int page, int pageSize, Guid? viewerUserId = null, CancellationToken ct = default)
    {
        if (!await _permissions.CanViewForumAsync(viewerUserId, forumId, ct))
            throw new PermissionDeniedException(ForumPermissions.ViewForum.ToString(), "forum");

        var posts = await _db.Posts
            .Include(p => p.Author)
            .Where(p => p.ForumId == forumId && p.ParentPostId == null)
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var authorIds = posts.Select(p => p.AuthorUserId).Distinct();
        var presentations = await _presentation.GetPresentationsAsync(authorIds, viewerUserId, forumId, ct);

        return posts.Select(p => ToDto(p, presentations[p.AuthorUserId], [])).ToList();
    }

    public async Task<PostDto?> GetThreadAsync(Guid postId, Guid? viewerUserId = null, CancellationToken ct = default)
    {
        var threadPosts = await LoadThreadPostsAsync(postId, ct);
        if (threadPosts.Count == 0) return null;

        var root = threadPosts.FirstOrDefault(p => p.Id == postId);
        if (root is null) return null;

        if (!await _permissions.CanViewForumAsync(viewerUserId, root.ForumId, ct))
            throw new PermissionDeniedException(ForumPermissions.ViewForum.ToString(), "forum");

        var authorIds = threadPosts.Select(p => p.AuthorUserId).Distinct();
        var presentations = await _presentation.GetPresentationsAsync(authorIds, viewerUserId, root.ForumId, ct);

        return BuildTree(root, threadPosts, presentations);
    }

    public async Task UpsertFederatedAsync(FederatedPostPayload payload, Guid sourceNodeId, string sourceNodeUrl, CancellationToken ct = default)
    {
        if (await _db.Posts.AnyAsync(p => p.Id == payload.Id, ct)) return;

        var forum = await _db.Forums.FirstOrDefaultAsync(f => f.Id == payload.ForumId, ct);
        if (forum is null)
        {
            await _forums.UpsertFederatedAsync(
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

        if (payload.ParentPostId.HasValue)
            await IncrementReplyCountsAsync(payload.ParentPostId.Value, ct);

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

        _db.Posts.Add(post);
        await _db.SaveChangesAsync(ct);
    }

    private async Task IncrementReplyCountsAsync(Guid parentPostId, CancellationToken ct)
    {
        var currentId = parentPostId;
        while (true)
        {
            var ancestor = await _db.Posts.FindAsync([currentId], ct);
            if (ancestor is null) break;

            ancestor.ReplyCount++;
            if (!ancestor.ParentPostId.HasValue) break;
            currentId = ancestor.ParentPostId.Value;
        }
    }

    private async Task<List<Post>> LoadThreadPostsAsync(Guid rootId, CancellationToken ct)
    {
        var postIds = await _db.Database
            .SqlQuery<Guid>($"""
                WITH RECURSIVE thread_posts AS (
                    SELECT "Id" FROM "Posts" WHERE "Id" = {rootId}
                    UNION ALL
                    SELECT p."Id" FROM "Posts" p
                    INNER JOIN thread_posts tp ON p."ParentPostId" = tp."Id"
                )
                SELECT "Id" FROM thread_posts
                """)
            .ToListAsync(ct);

        if (postIds.Count == 0) return [];

        return await _db.Posts
            .Include(p => p.Author)
            .Where(p => postIds.Contains(p.Id))
            .ToListAsync(ct);
    }

    private static PostDto BuildTree(Post root, List<Post> threadPosts, IReadOnlyDictionary<Guid, UserPresentationDto> presentations)
    {
        var lookup = threadPosts
            .GroupBy(p => p.ParentPostId ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => g.ToList());
        return BuildNode(root, lookup, presentations);
    }

    private static PostDto BuildNode(Post post, Dictionary<Guid, List<Post>> lookup, IReadOnlyDictionary<Guid, UserPresentationDto> presentations)
    {
        var children = lookup.GetValueOrDefault(post.Id, [])
            .OrderBy(p => p.CreatedAt)
            .Select(p => BuildNode(p, lookup, presentations))
            .ToList();

        return ToDto(post, presentations[post.AuthorUserId], children);
    }

    public static PostDto ToDto(Post post, UserPresentationDto author, List<PostDto> replies) => new(
        post.Id,
        post.ForumId,
        post.ParentPostId,
        author,
        post.Message,
        post.ReplyCount,
        post.OriginNodeUrl,
        post.IsFederated,
        post.CreatedAt,
        replies);
}
