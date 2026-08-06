using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Domain.Ranking;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IPostService
{
    Task<PostDto> CreateAsync(Guid userId, CreatePostRequest request, Guid? viewerUserId = null, CancellationToken ct = default);
    Task<PostDto> UpdateAsync(Guid userId, Guid postId, UpdatePostRequest request, Guid? viewerUserId = null, CancellationToken ct = default);
    Task<PostDto> SoftDeleteAsync(Guid userId, Guid postId, Guid? viewerUserId = null, CancellationToken ct = default);
    Task HardDeleteAsync(Guid userId, Guid postId, CancellationToken ct = default);
    Task<IReadOnlyList<PostRevisionDto>> GetRevisionsAsync(Guid userId, Guid postId, CancellationToken ct = default);
    Task<Post?> GetEntityAsync(Guid postId, CancellationToken ct = default);
    Task<IReadOnlyList<PostDto>> GetForumThreadsAsync(
        Guid forumId,
        int page,
        int pageSize,
        PostSort sort,
        Guid? viewerUserId = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<RecentThreadDto>> GetRecentThreadsAsync(
        int page,
        int pageSize,
        PostSort sort,
        Guid? viewerUserId = null,
        CancellationToken ct = default);
    Task<PostDto?> GetThreadAsync(Guid postId, Guid? viewerUserId = null, CancellationToken ct = default);
    Task<PostDto> SetThreadLockedAsync(Guid userId, Guid postId, bool locked, Guid? viewerUserId = null, CancellationToken ct = default);
    Task<PostDto> SetThreadPinnedAsync(Guid userId, Guid postId, bool pinned, Guid? viewerUserId = null, CancellationToken ct = default);
    Task UpsertFederatedAsync(FederatedPostPayload payload, Guid sourceNodeId, string sourceNodeUrl, CancellationToken ct = default);
    Task ApplyFederatedUpdateAsync(FederatedPostUpdatePayload payload, CancellationToken ct = default);
    Task ApplyFederatedDeleteAsync(FederatedPostDeletePayload payload, CancellationToken ct = default);
}

public class PostService : IPostService
{
    private const string DeletedMessage = "[deleted]";

    private readonly SneklDbContext _db;
    private readonly IUserPresentationService _presentation;
    private readonly IPermissionService _permissions;
    private readonly IForumService _forums;
    private readonly IPostUpvoteService _upvotes;
    private readonly INodeService _node;
    private readonly IForumModerationService _moderation;

    public PostService(
        SneklDbContext db,
        IUserPresentationService presentation,
        IPermissionService permissions,
        IForumService forums,
        IPostUpvoteService upvotes,
        INodeService node,
        IForumModerationService moderation)
    {
        _db = db;
        _presentation = presentation;
        _permissions = permissions;
        _forums = forums;
        _upvotes = upvotes;
        _node = node;
        _moderation = moderation;
    }

    public async Task<PostDto> CreateAsync(Guid userId, CreatePostRequest request, Guid? viewerUserId = null, CancellationToken ct = default)
    {
        var forum = await _db.Forums.FindAsync([request.ForumId], ct)
            ?? throw new InvalidOperationException("Forum not found.");

        if (request.ParentPostId.HasValue)
            await _permissions.RequireForumPermissionAsync(userId, forum.Id, ForumPermissions.Reply, ct);
        else
            await _permissions.RequireForumPermissionAsync(userId, forum.Id, ForumPermissions.CreateThread, ct);

        if (await _moderation.IsBannedAsync(forum.Id, userId, ct))
            throw new InvalidOperationException("You are banned from this forum.");

        if (request.ParentPostId.HasValue)
        {
            var parent = await _db.Posts.FindAsync([request.ParentPostId.Value], ct)
                ?? throw new InvalidOperationException("Parent post not found.");

            if (parent.ForumId != forum.Id)
                throw new InvalidOperationException("Parent post belongs to a different forum.");

            var rootId = await GetThreadRootIdAsync(parent.Id, ct);
            var root = await _db.Posts.FindAsync([rootId], ct);
            if (root?.IsLocked == true)
            {
                var perms = await _permissions.GetForumPermissionsAsync(userId, forum.Id, ct);
                if (!perms.HasFlag(ForumPermissions.ManagePosts))
                    throw new InvalidOperationException("This thread is locked.");
            }

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
        return await ToDtoAsync(post, authorPresentation, [], new HashSet<Guid>(), viewerUserId, ct);
    }

    public async Task<PostDto> UpdateAsync(
        Guid userId,
        Guid postId,
        UpdatePostRequest request,
        Guid? viewerUserId = null,
        CancellationToken ct = default)
    {
        var post = await _db.Posts.Include(p => p.Author).FirstOrDefaultAsync(p => p.Id == postId, ct)
            ?? throw new InvalidOperationException("Post not found.");

        if (!post.IsLocallyOwned)
            throw new InvalidOperationException("Federated posts must be edited on the origin node.");

        if (post.IsDeleted)
            throw new InvalidOperationException("Deleted posts cannot be edited.");

        var message = request.Message.Trim();
        if (string.IsNullOrWhiteSpace(message))
            throw new InvalidOperationException("Message is required.");

        var perms = await _permissions.GetForumPermissionsAsync(userId, post.ForumId, ct);
        var isAuthor = post.AuthorUserId == userId;
        if (!(isAuthor && perms.HasFlag(ForumPermissions.EditOwnPost)) && !perms.HasFlag(ForumPermissions.ManagePosts))
            throw new PermissionDeniedException(ForumPermissions.EditOwnPost.ToString(), "forum");

        if (!string.Equals(post.Message, message, StringComparison.Ordinal))
        {
            _db.PostRevisions.Add(new PostRevision
            {
                PostId = post.Id,
                EditedByUserId = userId,
                Message = post.Message
            });
            post.Message = message;
            post.EditedAt = DateTime.UtcNow;
            post.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        var authorPresentation = await _presentation.GetPresentationAsync(
            post.AuthorUserId,
            viewerUserId ?? userId,
            post.ForumId,
            ct);
        return await ToDtoAsync(post, authorPresentation, [], new HashSet<Guid>(), viewerUserId, ct);
    }

    public async Task<PostDto> SoftDeleteAsync(
        Guid userId,
        Guid postId,
        Guid? viewerUserId = null,
        CancellationToken ct = default)
    {
        var post = await _db.Posts.Include(p => p.Author).FirstOrDefaultAsync(p => p.Id == postId, ct)
            ?? throw new InvalidOperationException("Post not found.");

        if (!post.IsLocallyOwned)
            throw new InvalidOperationException("Federated posts must be deleted on the origin node.");

        if (post.IsDeleted)
            throw new InvalidOperationException("Post is already deleted.");

        var perms = await _permissions.GetForumPermissionsAsync(userId, post.ForumId, ct);
        var isAuthor = post.AuthorUserId == userId;
        if (!(isAuthor && perms.HasFlag(ForumPermissions.DeleteOwnPost)) && !perms.HasFlag(ForumPermissions.ManagePosts))
            throw new PermissionDeniedException(ForumPermissions.DeleteOwnPost.ToString(), "forum");

        post.IsDeleted = true;
        post.DeletedAt = DateTime.UtcNow;
        post.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var authorPresentation = await _presentation.GetPresentationAsync(
            post.AuthorUserId,
            viewerUserId ?? userId,
            post.ForumId,
            ct);
        return await ToDtoAsync(post, authorPresentation, [], new HashSet<Guid>(), viewerUserId, ct);
    }

    public async Task HardDeleteAsync(Guid userId, Guid postId, CancellationToken ct = default)
    {
        var post = await _db.Posts.FindAsync([postId], ct)
            ?? throw new InvalidOperationException("Post not found.");

        if (!post.IsLocallyOwned)
            throw new InvalidOperationException("Federated posts must be deleted on the origin node.");

        await _permissions.RequireForumPermissionAsync(userId, post.ForumId, ForumPermissions.ManagePosts, ct);

        var revisions = await _db.PostRevisions.Where(r => r.PostId == postId).ToListAsync(ct);
        _db.PostRevisions.RemoveRange(revisions);

        var upvotes = await _db.PostUpvotes.Where(u => u.PostId == postId).ToListAsync(ct);
        _db.PostUpvotes.RemoveRange(upvotes);

        _db.Posts.Remove(post);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PostRevisionDto>> GetRevisionsAsync(Guid userId, Guid postId, CancellationToken ct = default)
    {
        var post = await _db.Posts.FindAsync([postId], ct)
            ?? throw new InvalidOperationException("Post not found.");

        var perms = await _permissions.GetForumPermissionsAsync(userId, post.ForumId, ct);
        var canView = post.AuthorUserId == userId || perms.HasFlag(ForumPermissions.ManagePosts);
        if (!canView)
            throw new PermissionDeniedException(ForumPermissions.ManagePosts.ToString(), "forum");

        var rows = await _db.PostRevisions
            .Include(r => r.EditedBy)
            .Where(r => r.PostId == postId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return rows
            .Select(r => new PostRevisionDto(
                r.Id,
                r.Message,
                r.EditedByUserId,
                r.EditedBy?.Username ?? "unknown",
                r.CreatedAt))
            .ToList();
    }

    public Task<Post?> GetEntityAsync(Guid postId, CancellationToken ct = default) =>
        _db.Posts.Include(p => p.Author).Include(p => p.Forum).FirstOrDefaultAsync(p => p.Id == postId, ct);

    public async Task<IReadOnlyList<PostDto>> GetForumThreadsAsync(
        Guid forumId,
        int page,
        int pageSize,
        PostSort sort,
        Guid? viewerUserId = null,
        CancellationToken ct = default)
    {
        if (!await _permissions.CanViewForumAsync(viewerUserId, forumId, ct))
            throw new PermissionDeniedException(ForumPermissions.ViewForum.ToString(), "forum");

        var posts = await LoadRootPostsAsync([forumId], sort, page, pageSize, ownedOnly: false, ct);
        return await MapRootPostsAsync(posts, forumId, viewerUserId, ct);
    }

    public async Task<IReadOnlyList<RecentThreadDto>> GetRecentThreadsAsync(
        int page,
        int pageSize,
        PostSort sort,
        Guid? viewerUserId = null,
        CancellationToken ct = default)
    {
        var localNode = await _node.GetOrCreateAsync(ct);

        var forumRows = await _db.Forums
            .AsNoTracking()
            .Include(f => f.ParentForum)
            .Select(f => new { f.Id, f.Slug, f.Name, ParentSlug = f.ParentForum != null ? f.ParentForum.Slug : null })
            .ToListAsync(ct);

        var visibleForumIds = new List<Guid>();
        var forumMeta = new Dictionary<Guid, (string Slug, string Name, string? ParentSlug)>();
        foreach (var forum in forumRows)
        {
            if (!await _permissions.CanViewForumAsync(viewerUserId, forum.Id, ct)) continue;
            visibleForumIds.Add(forum.Id);
            forumMeta[forum.Id] = (forum.Slug, forum.Name, forum.ParentSlug);
        }

        if (visibleForumIds.Count == 0) return [];

        var posts = await LoadRootPostsAsync(visibleForumIds, sort, page, pageSize, ownedOnly: true, ct);
        var results = new List<RecentThreadDto>();

        foreach (var post in posts)
        {
            if (!forumMeta.TryGetValue(post.ForumId, out var meta)) continue;

            var mapped = await MapRootPostsAsync([post], post.ForumId, viewerUserId, ct);
            if (mapped.Count == 0) continue;

            results.Add(new RecentThreadDto(
                mapped[0],
                meta.Slug,
                meta.Name,
                meta.ParentSlug,
                localNode.BaseUrl,
                localNode.Did));
        }

        return results;
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
        var viewerUpvotes = await _upvotes.GetViewerUpvotedPostIdsAsync(threadPosts.Select(p => p.Id), viewerUserId, ct);

        return await BuildTreeAsync(root, threadPosts, presentations, viewerUpvotes, viewerUserId, ct);
    }

    public async Task<PostDto> SetThreadLockedAsync(
        Guid userId,
        Guid postId,
        bool locked,
        Guid? viewerUserId = null,
        CancellationToken ct = default)
    {
        var post = await _db.Posts.Include(p => p.Author).FirstOrDefaultAsync(p => p.Id == postId, ct)
            ?? throw new InvalidOperationException("Post not found.");

        if (!post.IsLocallyOwned)
            throw new InvalidOperationException("Federated posts must be moderated on the origin node.");

        if (post.ParentPostId.HasValue)
            throw new InvalidOperationException("Only thread roots can be locked.");

        await _permissions.RequireForumPermissionAsync(userId, post.ForumId, ForumPermissions.ManagePosts, ct);

        post.IsLocked = locked;
        post.LockedAt = locked ? DateTime.UtcNow : null;
        post.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var authorPresentation = await _presentation.GetPresentationAsync(
            post.AuthorUserId,
            viewerUserId ?? userId,
            post.ForumId,
            ct);
        return await ToDtoAsync(post, authorPresentation, [], new HashSet<Guid>(), viewerUserId, ct);
    }

    public async Task<PostDto> SetThreadPinnedAsync(
        Guid userId,
        Guid postId,
        bool pinned,
        Guid? viewerUserId = null,
        CancellationToken ct = default)
    {
        var post = await _db.Posts.Include(p => p.Author).FirstOrDefaultAsync(p => p.Id == postId, ct)
            ?? throw new InvalidOperationException("Post not found.");

        if (!post.IsLocallyOwned)
            throw new InvalidOperationException("Federated posts must be moderated on the origin node.");

        if (post.ParentPostId.HasValue)
            throw new InvalidOperationException("Only thread roots can be pinned.");

        await _permissions.RequireForumPermissionAsync(userId, post.ForumId, ForumPermissions.ManagePosts, ct);

        post.IsPinned = pinned;
        post.PinnedAt = pinned ? DateTime.UtcNow : null;
        post.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var authorPresentation = await _presentation.GetPresentationAsync(
            post.AuthorUserId,
            viewerUserId ?? userId,
            post.ForumId,
            ct);
        return await ToDtoAsync(post, authorPresentation, [], new HashSet<Guid>(), viewerUserId, ct);
    }

    public async Task ApplyFederatedUpdateAsync(FederatedPostUpdatePayload payload, CancellationToken ct = default)
    {
        var post = await _db.Posts.FindAsync([payload.Id], ct);
        if (post is null || !post.IsFederated) return;

        if (post.IsDeleted) return;

        if (!string.Equals(post.Message, payload.Message, StringComparison.Ordinal))
        {
            _db.PostRevisions.Add(new PostRevision
            {
                PostId = post.Id,
                EditedByUserId = post.AuthorUserId,
                Message = post.Message
            });
            post.Message = payload.Message;
        }

        post.EditedAt = payload.EditedAt;
        post.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ApplyFederatedDeleteAsync(FederatedPostDeletePayload payload, CancellationToken ct = default)
    {
        var post = await _db.Posts.FindAsync([payload.Id], ct);
        if (post is null || !post.IsFederated || post.IsDeleted) return;

        post.IsDeleted = true;
        post.DeletedAt = payload.DeletedAt;
        post.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
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

    private async Task<IReadOnlyList<PostDto>> MapRootPostsAsync(
        IReadOnlyList<Post> posts,
        Guid forumId,
        Guid? viewerUserId,
        CancellationToken ct)
    {
        if (posts.Count == 0) return [];

        var authorIds = posts.Select(p => p.AuthorUserId).Distinct();
        var presentations = await _presentation.GetPresentationsAsync(authorIds, viewerUserId, forumId, ct);
        var viewerUpvotes = await _upvotes.GetViewerUpvotedPostIdsAsync(posts.Select(p => p.Id), viewerUserId, ct);

        var results = new List<PostDto>();
        foreach (var post in posts)
        {
            results.Add(await ToDtoAsync(post, presentations[post.AuthorUserId], [], viewerUpvotes, viewerUserId, ct));
        }

        return results;
    }

    private async Task<List<Post>> LoadRootPostsAsync(
        IReadOnlyList<Guid> forumIds,
        PostSort sort,
        int page,
        int pageSize,
        bool ownedOnly,
        CancellationToken ct)
    {
        if (forumIds.Count == 0) return [];

        if (sort == PostSort.New)
        {
            return await _db.Posts
                .Include(p => p.Author)
                .Where(p => forumIds.Contains(p.ForumId) && p.ParentPostId == null && !p.IsDeleted && (!ownedOnly || p.OriginNodeId == null))
                .OrderByDescending(p => p.IsPinned)
                .ThenByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);
        }

        var offset = (page - 1) * pageSize;
        var ownedFilter = ownedOnly ? "AND p.\"OriginNodeId\" IS NULL" : string.Empty;
        var forumIdList = string.Join(", ", forumIds.Select(id => $"'{id:D}'"));
        var ageOffset = PostRanking.AgeOffsetHours.ToString(CultureInfo.InvariantCulture);
        var gravity = PostRanking.Gravity.ToString(CultureInfo.InvariantCulture);
#pragma warning disable EF1002 // forumIds, offset, and pageSize are internal values — not user input
        var ids = await _db.Database
            .SqlQueryRaw<Guid>($"""
                SELECT p."Id"
                FROM "Posts" p
                WHERE p."ForumId" IN ({forumIdList}) AND p."ParentPostId" IS NULL AND p."IsDeleted" = false {ownedFilter}
                ORDER BY p."IsPinned" DESC,
                    p."UpvoteCount"::float8 / POWER(
                    GREATEST(EXTRACT(EPOCH FROM (NOW() AT TIME ZONE 'utc' - p."CreatedAt" AT TIME ZONE 'utc')) / 3600.0, 0) + {ageOffset},
                    {gravity}) DESC
                OFFSET {offset} LIMIT {pageSize}
                """)
            .ToListAsync(ct);
#pragma warning restore EF1002

        if (ids.Count == 0) return [];

        var posts = await _db.Posts
            .Include(p => p.Author)
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(ct);

        var order = ids.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        return posts.OrderBy(p => order[p.Id]).ToList();
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

    private async Task<PostDto> BuildTreeAsync(
        Post root,
        List<Post> threadPosts,
        IReadOnlyDictionary<Guid, UserPresentationDto> presentations,
        IReadOnlySet<Guid> viewerUpvotes,
        Guid? viewerUserId,
        CancellationToken ct)
    {
        var lookup = threadPosts
            .GroupBy(p => p.ParentPostId ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => g.ToList());
        return await BuildNodeAsync(root, lookup, presentations, viewerUpvotes, viewerUserId, ct);
    }

    private async Task<PostDto> BuildNodeAsync(
        Post post,
        Dictionary<Guid, List<Post>> lookup,
        IReadOnlyDictionary<Guid, UserPresentationDto> presentations,
        IReadOnlySet<Guid> viewerUpvotes,
        Guid? viewerUserId,
        CancellationToken ct)
    {
        var children = lookup.GetValueOrDefault(post.Id, [])
            .OrderBy(p => p.CreatedAt)
            .ToList();

        var childDtos = new List<PostDto>();
        foreach (var child in children)
        {
            childDtos.Add(await BuildNodeAsync(child, lookup, presentations, viewerUpvotes, viewerUserId, ct));
        }

        return await ToDtoAsync(post, presentations[post.AuthorUserId], childDtos, viewerUpvotes, viewerUserId, ct);
    }

    private async Task<Guid> GetThreadRootIdAsync(Guid postId, CancellationToken ct)
    {
        var currentId = postId;
        while (true)
        {
            var post = await _db.Posts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == currentId, ct)
                ?? throw new InvalidOperationException("Post not found.");
            if (!post.ParentPostId.HasValue) return post.Id;
            currentId = post.ParentPostId.Value;
        }
    }

    private async Task<PostViewerActionsDto?> BuildViewerActionsAsync(
        Post post,
        Guid? viewerUserId,
        CancellationToken ct)
    {
        if (!viewerUserId.HasValue || !post.IsLocallyOwned) return null;

        var perms = await _permissions.GetForumPermissionsAsync(viewerUserId, post.ForumId, ct);
        var isAuthor = post.AuthorUserId == viewerUserId.Value;
        var isRoot = !post.ParentPostId.HasValue;
        var canManage = perms.HasFlag(ForumPermissions.ManagePosts);
        var canEdit = !post.IsDeleted &&
                      ((isAuthor && perms.HasFlag(ForumPermissions.EditOwnPost)) || canManage);
        var canSoftDelete = !post.IsDeleted &&
                            ((isAuthor && perms.HasFlag(ForumPermissions.DeleteOwnPost)) || canManage);
        var canHardDelete = post.IsDeleted && canManage;
        var hasHistory = await _db.PostRevisions.AnyAsync(r => r.PostId == post.Id, ct);
        var canViewHistory = hasHistory && (isAuthor || canManage);
        var canLock = isRoot && canManage && !post.IsLocked;
        var canUnlock = isRoot && canManage && post.IsLocked;
        var canPin = isRoot && canManage && !post.IsPinned;
        var canUnpin = isRoot && canManage && post.IsPinned;
        var canReport = !isAuthor && !post.IsDeleted && perms.HasFlag(ForumPermissions.ViewForum);

        return new PostViewerActionsDto(
            canEdit, canSoftDelete, canHardDelete, canViewHistory,
            canLock, canUnlock, canPin, canUnpin, canReport);
    }

    private async Task<PostDto> ToDtoAsync(
        Post post,
        UserPresentationDto author,
        List<PostDto> replies,
        IReadOnlySet<Guid> viewerUpvotes,
        Guid? viewerUserId,
        CancellationToken ct)
    {
        var actions = await BuildViewerActionsAsync(post, viewerUserId, ct);
        return ToDto(post, author, replies, viewerUpvotes, actions);
    }

    public static PostDto ToDto(
        Post post,
        UserPresentationDto author,
        List<PostDto> replies,
        IReadOnlySet<Guid> viewerUpvotes,
        PostViewerActionsDto? viewerActions = null) => new(
        post.Id,
        post.ForumId,
        post.ParentPostId,
        author,
        post.IsDeleted ? DeletedMessage : post.Message,
        post.ReplyCount,
        post.UpvoteCount,
        viewerUpvotes.Contains(post.Id),
        post.OriginNodeUrl,
        post.IsFederated,
        post.IsDeleted,
        post.IsLocked,
        post.IsPinned,
        post.EditedAt,
        post.CreatedAt,
        replies,
        viewerActions);
}
