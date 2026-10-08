using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Domain.Ranking;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Search;

namespace Snekl.Infrastructure.Services;

public interface IPostService
{
    Task<PostDto> CreateAsync(Guid userId, CreatePostRequest request, Guid? viewerUserId = null, CancellationToken ct = default);
    Task<PostDto> UpdateAsync(Guid userId, Guid postId, UpdatePostRequest request, Guid? viewerUserId = null, CancellationToken ct = default);
    Task<PostDto> SoftDeleteAsync(Guid userId, Guid postId, Guid? viewerUserId = null, CancellationToken ct = default);
    Task HardDeleteAsync(Guid userId, Guid postId, CancellationToken ct = default);
    Task<IReadOnlyList<PostRevisionDto>> GetRevisionsAsync(Guid userId, Guid postId, CancellationToken ct = default);
    Task<Post?> GetEntityAsync(Guid postId, CancellationToken ct = default);
    Task<Guid> GetThreadRootIdAsync(Guid postId, CancellationToken ct = default);
    Task<IReadOnlyList<PostDto>> GetForumThreadsAsync(
        Guid forumId,
        int page,
        int pageSize,
        PostSort sort,
        Guid? viewerUserId = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<PostDto>> GetWatercoolerStreamAsync(
        Guid watercoolerId,
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
    Task<IReadOnlyList<SearchHitDto>> SearchAsync(
        string? query,
        int page,
        int pageSize,
        Guid? viewerUserId = null,
        CancellationToken ct = default);
    Task<PostDto?> GetThreadAsync(Guid postId, Guid? viewerUserId = null, CancellationToken ct = default);
    Task<PostDto> SetThreadLockedAsync(Guid userId, Guid postId, bool locked, Guid? viewerUserId = null, CancellationToken ct = default);
    Task<PostDto> SetThreadPinnedAsync(Guid userId, Guid postId, bool pinned, Guid? viewerUserId = null, CancellationToken ct = default);
    Task<PostDto> SetReplyPolicyAsync(Guid userId, Guid postId, string replyPolicy, Guid? viewerUserId = null, CancellationToken ct = default);
    Task<PostDto> SetMediaNsfwAsync(
        Guid userId,
        Guid postId,
        Guid mediaAssetId,
        UpdatePostMediaNsfwRequest request,
        Guid? viewerUserId = null,
        CancellationToken ct = default);
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
    private readonly IMediaService _media;
    private readonly IEmoteService _emotes;
    private readonly IPostSearchIndex _searchIndex;
    private readonly IFollowGraphClient _followGraph;
    private readonly INsfwAgeCheckService _nsfwAge;
    private readonly IReplyAlertService _replyAlerts;
    private readonly ILogger<PostService> _logger;

    public PostService(
        SneklDbContext db,
        IUserPresentationService presentation,
        IPermissionService permissions,
        IForumService forums,
        IPostUpvoteService upvotes,
        INodeService node,
        IForumModerationService moderation,
        IMediaService media,
        IEmoteService emotes,
        IPostSearchIndex searchIndex,
        IFollowGraphClient followGraph,
        INsfwAgeCheckService nsfwAge,
        IReplyAlertService replyAlerts,
        ILogger<PostService> logger)
    {
        _db = db;
        _presentation = presentation;
        _permissions = permissions;
        _forums = forums;
        _upvotes = upvotes;
        _node = node;
        _moderation = moderation;
        _media = media;
        _emotes = emotes;
        _searchIndex = searchIndex;
        _followGraph = followGraph;
        _nsfwAge = nsfwAge;
        _replyAlerts = replyAlerts;
        _logger = logger;
    }

    public async Task<PostDto> CreateAsync(Guid userId, CreatePostRequest request, Guid? viewerUserId = null, CancellationToken ct = default)
    {
        if (request.WatercoolerId.HasValue)
            return await CreateWatercoolerPostAsync(userId, request, viewerUserId, ct);

        if (!request.ForumId.HasValue)
            throw new InvalidOperationException("Nest or watercooler is required.");

        var forum = await _db.Forums.FindAsync([request.ForumId.Value], ct)
            ?? throw new InvalidOperationException("Nest not found.");

        if (!ForumApprovalStatuses.IsApproved(forum.ApprovalStatus))
            throw new InvalidOperationException("This nest is waiting for approval and cannot be used yet.");

        await _nsfwAge.EnsureViewerMayEnterForumAsync(userId, forum.Id, ct);

        if (request.ParentPostId.HasValue)
            await _permissions.RequireForumPermissionAsync(userId, forum.Id, ForumPermissions.Reply, ct);
        else
            await _permissions.RequireForumPermissionAsync(userId, forum.Id, ForumPermissions.CreateThread, ct);

        if (await _moderation.IsBannedAsync(forum.Id, userId, ct))
            throw new InvalidOperationException("You are banned from this nest.");

        var message = request.Message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message))
            throw new InvalidOperationException("Message is required.");

        string? title = null;
        if (!request.ParentPostId.HasValue)
        {
            title = request.Title?.Trim();
            if (string.IsNullOrWhiteSpace(title))
                throw new InvalidOperationException("Title is required for new threads.");
        }

        if (request.ParentPostId.HasValue)
        {
            var parent = await _db.Posts.FindAsync([request.ParentPostId.Value], ct)
                ?? throw new InvalidOperationException("Parent post not found.");

            if (parent.ForumId != forum.Id)
                throw new InvalidOperationException("Parent post belongs to a different nest.");

            var rootId = await GetThreadRootIdAsync(parent.Id, ct);
            var root = await _db.Posts.FindAsync([rootId], ct);
            if (root?.IsLocked == true)
            {
                var perms = await _permissions.GetForumPermissionsAsync(userId, forum.Id, ct);
                if (!perms.HasFlag(ForumPermissions.ManagePosts))
                    throw new InvalidOperationException("This thread is locked.");
            }

            await EnsureCanReplyUnderPolicyAsync(root, userId, ct);

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
            Title = title,
            Message = message,
            ReplyPolicy = request.ParentPostId.HasValue
                ? ReplyPolicies.Everyone
                : ReplyPolicies.Normalize(request.ReplyPolicy)
        };

        _db.Posts.Add(post);
        await _db.SaveChangesAsync(ct);

        if (request.MediaAssetIds is { Count: > 0 })
            await _media.AttachToPostAsync(post.Id, userId, request.MediaAssetIds, request.NsfwMedia, ct);

        await TryIndexPostAsync(post, ct);
        await TryRecordReplyAlertAsync(post, ct);

        var authorPresentation = await _presentation.GetPresentationAsync(userId, viewerUserId ?? userId, forum.Id, ct);
        return await ToDtoAsync(post, authorPresentation, [], new HashSet<Guid>(), viewerUserId, ct);
    }

    private async Task<PostDto> CreateWatercoolerPostAsync(
        Guid userId,
        CreatePostRequest request,
        Guid? viewerUserId,
        CancellationToken ct)
    {
        var cooler = await _db.Watercoolers.FindAsync([request.WatercoolerId!.Value], ct)
            ?? throw new InvalidOperationException("Watercooler not found.");

        if (!ForumApprovalStatuses.IsApproved(cooler.ApprovalStatus))
            throw new InvalidOperationException("This watercooler is waiting for approval and cannot be used yet.");

        await _permissions.RequireWatercoolerPermissionAsync(userId, cooler.Id, WatercoolerPermissions.SpeakWatercooler, ct);

        if (cooler.ParentForumId.HasValue && await _moderation.IsBannedAsync(cooler.ParentForumId.Value, userId, ct))
            throw new InvalidOperationException("You are banned from this nest.");

        if (cooler.ParentForumId.HasValue)
            await _nsfwAge.EnsureViewerMayEnterForumAsync(userId, cooler.ParentForumId.Value, ct);

        var message = request.Message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message))
            throw new InvalidOperationException("Message is required.");

        Guid? quotedId = request.QuotedPostId;
        if (quotedId.HasValue)
        {
            var quoted = await _db.Posts.FindAsync([quotedId.Value], ct)
                ?? throw new InvalidOperationException("Quoted post not found.");
            if (!quoted.WatercoolerId.HasValue)
                throw new InvalidOperationException("Only watercooler posts can be quoted.");
            if (!await _permissions.CanViewWatercoolerAsync(userId, quoted.WatercoolerId.Value, ct))
                throw new PermissionDeniedException(nameof(WatercoolerPermissions.ViewWatercooler), "watercooler");
            quoted.QuoteCount++;
        }

        if (request.ParentPostId.HasValue)
        {
            var parent = await _db.Posts.FindAsync([request.ParentPostId.Value], ct)
                ?? throw new InvalidOperationException("Parent post not found.");
            if (parent.WatercoolerId != cooler.Id)
                throw new InvalidOperationException("Parent post belongs to a different watercooler.");

            var rootId = await GetThreadRootIdAsync(parent.Id, ct);
            var root = await _db.Posts.FindAsync([rootId], ct);
            if (root?.IsLocked == true)
            {
                var perms = await _permissions.GetWatercoolerPermissionsAsync(userId, cooler.Id, ct);
                if (!perms.HasFlag(WatercoolerPermissions.ManageWatercooler))
                    throw new InvalidOperationException("This conversation is locked.");
            }

            await EnsureCanReplyUnderPolicyAsync(root, userId, ct);

            await IncrementReplyCountsAsync(parent.Id, ct);
        }

        var author = await _db.Users.FindAsync([userId], ct);
        var post = new Post
        {
            WatercoolerId = cooler.Id,
            ParentPostId = request.ParentPostId,
            QuotedPostId = quotedId,
            AuthorUserId = userId,
            AuthorDid = author?.Did,
            Message = message,
            ReplyPolicy = request.ParentPostId.HasValue
                ? ReplyPolicies.Everyone
                : ReplyPolicies.Normalize(request.ReplyPolicy)
        };
        _db.Posts.Add(post);
        await _db.SaveChangesAsync(ct);

        if (request.MediaAssetIds is { Count: > 0 })
            await _media.AttachToPostAsync(post.Id, userId, request.MediaAssetIds, nsfwHints: null, ct);

        await TryIndexPostAsync(post, ct);
        await TryRecordReplyAlertAsync(post, ct);

        var authorPresentation = await _presentation.GetPresentationAsync(
            userId, viewerUserId ?? userId, cooler.ParentForumId, ct);
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

        var canEdit = await CanModerateOrEditOwnAsync(userId, post, edit: true, ct);
        if (!canEdit)
            throw new PermissionDeniedException("EditOwnPost", post.WatercoolerId.HasValue ? "watercooler" : "forum");

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
            await TryIndexPostAsync(post, ct);
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

        var canDelete = await CanModerateOrEditOwnAsync(userId, post, edit: false, ct);
        if (!canDelete)
            throw new PermissionDeniedException("DeleteOwnPost", post.WatercoolerId.HasValue ? "watercooler" : "forum");

        post.IsDeleted = true;
        post.DeletedAt = DateTime.UtcNow;
        post.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await TryIndexPostAsync(post, ct);

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

        await RequireModerateAsync(userId, post, ct);

        var revisions = await _db.PostRevisions.Where(r => r.PostId == postId).ToListAsync(ct);
        _db.PostRevisions.RemoveRange(revisions);

        var upvotes = await _db.PostUpvotes.Where(u => u.PostId == postId).ToListAsync(ct);
        _db.PostUpvotes.RemoveRange(upvotes);

        var reactions = await _db.PostReactions.Where(r => r.PostId == postId).ToListAsync(ct);
        _db.PostReactions.RemoveRange(reactions);

        _db.Posts.Remove(post);
        await _db.SaveChangesAsync(ct);
        await TryDeleteFromIndexAsync(postId, ct);
    }

    public async Task<IReadOnlyList<PostRevisionDto>> GetRevisionsAsync(Guid userId, Guid postId, CancellationToken ct = default)
    {
        var post = await _db.Posts.FindAsync([postId], ct)
            ?? throw new InvalidOperationException("Post not found.");

        var canView = post.AuthorUserId == userId || await CanViewHistoryAsync(userId, post, ct);
        if (!canView)
            throw new PermissionDeniedException(
                post.WatercoolerId.HasValue
                    ? nameof(WatercoolerPermissions.ViewHistory)
                    : nameof(ForumPermissions.ViewHistory),
                post.WatercoolerId.HasValue ? "watercooler" : "forum");

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
        _db.Posts.Include(p => p.Author).Include(p => p.Forum).Include(p => p.Watercooler).FirstOrDefaultAsync(p => p.Id == postId, ct);

    public Task<Guid> GetThreadRootIdAsync(Guid postId, CancellationToken ct = default) =>
        ResolveThreadRootIdAsync(postId, ct);

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

        await _nsfwAge.EnsureViewerMayEnterForumAsync(viewerUserId, forumId, ct);

        var posts = await LoadRootPostsAsync([forumId], sort, page, pageSize, ownedOnly: false, ct);
        return await MapRootPostsAsync(posts, forumId, viewerUserId, ct);
    }

    public async Task<IReadOnlyList<PostDto>> GetWatercoolerStreamAsync(
        Guid watercoolerId,
        int page,
        int pageSize,
        PostSort sort,
        Guid? viewerUserId = null,
        CancellationToken ct = default)
    {
        if (!await _permissions.CanViewWatercoolerAsync(viewerUserId, watercoolerId, ct))
            throw new PermissionDeniedException(nameof(WatercoolerPermissions.ViewWatercooler), "watercooler");

        var cooler = await _db.Watercoolers.AsNoTracking().FirstOrDefaultAsync(w => w.Id == watercoolerId, ct)
            ?? throw new InvalidOperationException("Watercooler not found.");

        if (cooler.ParentForumId.HasValue)
            await _nsfwAge.EnsureViewerMayEnterForumAsync(viewerUserId, cooler.ParentForumId.Value, ct);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        IQueryable<Post> query = _db.Posts.Include(p => p.Author)
            .Where(p => p.WatercoolerId == watercoolerId && !p.IsDeleted);

        if (cooler.AllowUpvotes)
        {
            query = query.Where(p => p.ParentPostId == null);
            if (sort == PostSort.New)
                query = query.OrderByDescending(p => p.IsPinned).ThenByDescending(p => p.CreatedAt);
            else
                query = query.OrderByDescending(p => p.IsPinned).ThenByDescending(p => p.UpvoteCount).ThenByDescending(p => p.CreatedAt);
        }
        else
        {
            query = cooler.NewestAtBottom
                ? query.OrderByDescending(p => p.CreatedAt)
                : query.OrderBy(p => p.CreatedAt);
        }

        List<Post> posts;
        if (!cooler.AllowUpvotes && cooler.NewestAtBottom)
        {
            posts = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            posts.Reverse();
        }
        else
        {
            posts = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        }

        return await MapRootPostsAsync(posts, cooler.ParentForumId, viewerUserId, ct);
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
            if (!await _nsfwAge.ViewerMayEnterForumAsync(viewerUserId, forum.Id, ct)) continue;
            visibleForumIds.Add(forum.Id);
            forumMeta[forum.Id] = (forum.Slug, forum.Name, forum.ParentSlug);
        }

        if (visibleForumIds.Count == 0) return [];

        var posts = await LoadRootPostsAsync(visibleForumIds, sort, page, pageSize, ownedOnly: true, ct);
        var results = new List<RecentThreadDto>();

        foreach (var post in posts)
        {
            if (post.ForumId is not Guid forumId || !forumMeta.TryGetValue(forumId, out var meta)) continue;

            var mapped = await MapRootPostsAsync([post], forumId, viewerUserId, ct);
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

    public async Task<IReadOnlyList<SearchHitDto>> SearchAsync(
        string? query,
        int page,
        int pageSize,
        Guid? viewerUserId = null,
        CancellationToken ct = default)
    {
        var tokens = TokenizeSearchQuery(query);
        if (tokens.Count == 0) return [];

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

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
            if (!await _nsfwAge.ViewerMayEnterForumAsync(viewerUserId, forum.Id, ct)) continue;
            visibleForumIds.Add(forum.Id);
            forumMeta[forum.Id] = (forum.Slug, forum.Name, forum.ParentSlug);
        }

        if (visibleForumIds.Count == 0) return [];

        if (_searchIndex.IsEnabled)
        {
            try
            {
                var osMatches = await _searchIndex.SearchAsync(query!.Trim(), visibleForumIds, page, pageSize, ct);
                return await HydrateSearchHitsAsync(osMatches, forumMeta, localNode, viewerUserId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenSearch query failed; falling back to Postgres");
            }
        }

        var matchesQuery = _db.Posts
            .AsNoTracking()
            .Include(p => p.Author)
            .Where(p => p.ForumId != null && visibleForumIds.Contains(p.ForumId.Value) && !p.IsDeleted);

        foreach (var token in tokens)
        {
            var pattern = "%" + EscapeLike(token) + "%";
            matchesQuery = matchesQuery.Where(p =>
                EF.Functions.ILike(p.Message, pattern) ||
                (p.Title != null && EF.Functions.ILike(p.Title, pattern)));
        }

        var matches = await matchesQuery
            .OrderByDescending(p => p.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        if (matches.Count == 0) return [];

        var byId = matches.ToDictionary(p => p.Id);
        var missing = matches
            .Where(p => p.ParentPostId.HasValue && !byId.ContainsKey(p.ParentPostId.Value))
            .Select(p => p.ParentPostId!.Value)
            .Distinct()
            .ToList();
        while (missing.Count > 0)
        {
            var parents = await _db.Posts
                .AsNoTracking()
                .Include(p => p.Author)
                .Where(p => missing.Contains(p.Id))
                .ToListAsync(ct);
            foreach (var parent in parents)
                byId.TryAdd(parent.Id, parent);
            missing = parents
                .Where(p => p.ParentPostId.HasValue && !byId.ContainsKey(p.ParentPostId.Value))
                .Select(p => p.ParentPostId!.Value)
                .Distinct()
                .ToList();
        }

        var orderedRoots = new List<Guid>();
        var snippetByRoot = new Dictionary<Guid, (string Snippet, bool MatchedInReply)>();
        foreach (var match in matches)
        {
            var rootId = RootIdFromLoaded(match, byId);
            if (rootId is null) continue;
            if (snippetByRoot.ContainsKey(rootId.Value)) continue;
            var text = match.Title is { Length: > 0 } title ? $"{title}\n{match.Message}" : match.Message;
            snippetByRoot[rootId.Value] = (MakeSnippet(text, tokens), match.ParentPostId.HasValue);
            orderedRoots.Add(rootId.Value);
        }

        var pageIds = orderedRoots.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        if (pageIds.Count == 0) return [];

        var missingRoots = pageIds.Where(id => !byId.ContainsKey(id) || byId[id].ParentPostId.HasValue).ToList();
        if (missingRoots.Count > 0)
        {
            var roots = await _db.Posts
                .AsNoTracking()
                .Include(p => p.Author)
                .Where(p => missingRoots.Contains(p.Id) && p.ParentPostId == null)
                .ToListAsync(ct);
            foreach (var root in roots)
                byId[root.Id] = root;
        }

        var hits = new List<SearchHitDto>();
        foreach (var rootId in pageIds)
        {
            if (!byId.TryGetValue(rootId, out var root) || root.ParentPostId.HasValue || root.IsDeleted)
                continue;
            if (root.ForumId is not Guid searchForumId || !forumMeta.TryGetValue(searchForumId, out var meta)) continue;
            var mapped = await MapRootPostsAsync([root], searchForumId, viewerUserId, ct);
            if (mapped.Count == 0) continue;
            snippetByRoot.TryGetValue(rootId, out var snippet);
            hits.Add(new SearchHitDto(
                new RecentThreadDto(
                    mapped[0],
                    meta.Slug,
                    meta.Name,
                    meta.ParentSlug,
                    localNode.BaseUrl,
                    localNode.Did),
                snippet.Snippet,
                snippet.MatchedInReply));
        }

        return hits;
    }

    private async Task<IReadOnlyList<SearchHitDto>> HydrateSearchHitsAsync(
        IReadOnlyList<PostSearchMatch> matches,
        IReadOnlyDictionary<Guid, (string Slug, string Name, string? ParentSlug)> forumMeta,
        NodeConfig localNode,
        Guid? viewerUserId,
        CancellationToken ct)
    {
        if (matches.Count == 0) return [];

        var rootIds = matches.Select(m => m.ThreadRootId).Distinct().ToList();
        var roots = await _db.Posts
            .AsNoTracking()
            .Include(p => p.Author)
            .Where(p => rootIds.Contains(p.Id) && p.ParentPostId == null && !p.IsDeleted)
            .ToListAsync(ct);
        var byId = roots.ToDictionary(p => p.Id);

        var hits = new List<SearchHitDto>();
        foreach (var match in matches)
        {
            if (!byId.TryGetValue(match.ThreadRootId, out var root)) continue;
            if (root.ForumId is not Guid searchForumId || !forumMeta.TryGetValue(searchForumId, out var meta)) continue;
            var mapped = await MapRootPostsAsync([root], searchForumId, viewerUserId, ct);
            if (mapped.Count == 0) continue;
            hits.Add(new SearchHitDto(
                new RecentThreadDto(
                    mapped[0],
                    meta.Slug,
                    meta.Name,
                    meta.ParentSlug,
                    localNode.BaseUrl,
                    localNode.Did),
                match.Snippet,
                match.MatchedInReply));
        }

        return hits;
    }

    public async Task<PostDto?> GetThreadAsync(Guid postId, Guid? viewerUserId = null, CancellationToken ct = default)
    {
        var probe = await _db.Posts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == postId, ct);
        if (probe is null) return null;
        var loadId = probe.WatercoolerId.HasValue ? await ResolveThreadRootIdAsync(postId, ct) : postId;
        var threadPosts = await LoadThreadPostsAsync(loadId, ct);
        if (threadPosts.Count == 0) return null;

        var focused = threadPosts.FirstOrDefault(p => p.Id == postId);
        if (focused is null) return null;

        if (focused.WatercoolerId.HasValue)
        {
            if (!await _permissions.CanViewWatercoolerAsync(viewerUserId, focused.WatercoolerId.Value, ct))
                throw new PermissionDeniedException(nameof(WatercoolerPermissions.ViewWatercooler), "watercooler");
            var parentForumId = await _db.Watercoolers.AsNoTracking()
                .Where(w => w.Id == focused.WatercoolerId.Value)
                .Select(w => w.ParentForumId)
                .FirstOrDefaultAsync(ct);
            if (parentForumId.HasValue)
                await _nsfwAge.EnsureViewerMayEnterForumAsync(viewerUserId, parentForumId.Value, ct);
        }
        else if (focused.ForumId.HasValue)
        {
            if (!await _permissions.CanViewForumAsync(viewerUserId, focused.ForumId.Value, ct))
                throw new PermissionDeniedException(ForumPermissions.ViewForum.ToString(), "forum");
            await _nsfwAge.EnsureViewerMayEnterForumAsync(viewerUserId, focused.ForumId.Value, ct);
        }
        else
        {
            return null;
        }

        var presentationForumId = focused.ForumId ?? focused.Watercooler?.ParentForumId;
        if (presentationForumId is null && focused.WatercoolerId.HasValue)
        {
            presentationForumId = await _db.Watercoolers.AsNoTracking()
                .Where(w => w.Id == focused.WatercoolerId.Value)
                .Select(w => w.ParentForumId)
                .FirstOrDefaultAsync(ct);
        }

        var authorIds = threadPosts.Select(p => p.AuthorUserId).Distinct();
        var presentations = await _presentation.GetPresentationsAsync(authorIds, viewerUserId, presentationForumId, ct);
        var viewerUpvotes = await _upvotes.GetViewerUpvotedPostIdsAsync(threadPosts.Select(p => p.Id), viewerUserId, ct);

        if (focused.WatercoolerId.HasValue)
            return await BuildConversationAsync(focused, threadPosts, presentations, viewerUpvotes, viewerUserId, ct);

        return await BuildTreeAsync(focused, threadPosts, presentations, viewerUpvotes, viewerUserId, ct);
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

        await RequireModerateAsync(userId, post, ct);

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

        await RequireModerateAsync(userId, post, ct);

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

    public async Task<PostDto> SetReplyPolicyAsync(
        Guid userId,
        Guid postId,
        string replyPolicy,
        Guid? viewerUserId = null,
        CancellationToken ct = default)
    {
        var post = await _db.Posts.Include(p => p.Author).FirstOrDefaultAsync(p => p.Id == postId, ct)
            ?? throw new InvalidOperationException("Post not found.");

        if (!post.IsLocallyOwned)
            throw new InvalidOperationException("Federated posts must be edited on the origin node.");

        if (post.ParentPostId.HasValue)
            throw new InvalidOperationException("Only thread roots can change who may reply.");

        var isAuthor = post.AuthorUserId == userId;
        if (!isAuthor && !await CanModerateAsync(userId, post, ct))
            throw new PermissionDeniedException("SetReplyPolicy", post.WatercoolerId.HasValue ? "watercooler" : "forum");

        post.ReplyPolicy = ReplyPolicies.Normalize(replyPolicy);
        post.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var authorPresentation = await _presentation.GetPresentationAsync(
            post.AuthorUserId,
            viewerUserId ?? userId,
            post.ForumId,
            ct);
        return await ToDtoAsync(post, authorPresentation, [], new HashSet<Guid>(), viewerUserId, ct, threadRoot: post);
    }

    public async Task<PostDto> SetMediaNsfwAsync(
        Guid userId,
        Guid postId,
        Guid mediaAssetId,
        UpdatePostMediaNsfwRequest request,
        Guid? viewerUserId = null,
        CancellationToken ct = default)
    {
        var post = await _db.Posts.Include(p => p.Author).FirstOrDefaultAsync(p => p.Id == postId, ct)
            ?? throw new InvalidOperationException("Post not found.");

        if (!post.IsLocallyOwned)
            throw new InvalidOperationException("Federated posts must be moderated on the origin node.");

        await _media.SetNsfwAsync(userId, postId, mediaAssetId, request.IsNsfw, request.CoverText, ct);

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
        await TryIndexPostAsync(post, ct);
    }

    public async Task ApplyFederatedDeleteAsync(FederatedPostDeletePayload payload, CancellationToken ct = default)
    {
        var post = await _db.Posts.FindAsync([payload.Id], ct);
        if (post is null || !post.IsFederated || post.IsDeleted) return;

        post.IsDeleted = true;
        post.DeletedAt = payload.DeletedAt;
        post.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await TryIndexPostAsync(post, ct);
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
            Title = payload.ParentPostId.HasValue ? null : payload.Title,
            Message = payload.Message,
            CreatedAt = payload.CreatedAt,
            UpdatedAt = payload.CreatedAt,
            OriginNodeId = sourceNodeId,
            OriginNodeUrl = sourceNodeUrl
        };

        _db.Posts.Add(post);
        await _db.SaveChangesAsync(ct);

        if (payload.AttachmentUrls is { Count: > 0 })
        {
            for (var i = 0; i < payload.AttachmentUrls.Count; i++)
            {
                var url = payload.AttachmentUrls[i];
                if (string.IsNullOrWhiteSpace(url)) continue;
                var asset = new MediaAsset
                {
                    UploaderUserId = author.Id,
                    StorageKey = $"external:{post.Id:N}:{i}",
                    ExternalUrl = url.Trim(),
                    ContentType = "image/*",
                    ByteSize = 0
                };
                _db.MediaAssets.Add(asset);
                _db.PostMedia.Add(new PostMedia
                {
                    PostId = post.Id,
                    MediaAssetId = asset.Id,
                    SortOrder = i
                });
            }

            await _db.SaveChangesAsync(ct);
        }

        await TryIndexPostAsync(post, ct);
    }

    private async Task<IReadOnlyList<PostDto>> MapRootPostsAsync(
        IReadOnlyList<Post> posts,
        Guid? forumId,
        Guid? viewerUserId,
        CancellationToken ct)
    {
        if (posts.Count == 0) return [];

        var rootIds = posts.Select(p => p.Id).ToList();
        var authorIds = posts.Select(p => p.AuthorUserId).Distinct().ToList();

        var directReplies = await _db.Posts
            .AsNoTracking()
            .Include(p => p.Author)
            .Where(p => p.ParentPostId != null && rootIds.Contains(p.ParentPostId.Value) && !p.IsDeleted)
            .ToListAsync(ct);

        var topReplies = directReplies
            .GroupBy(p => p.ParentPostId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(p => PostRanking.HotScore(p.UpvoteCount, p.CreatedAt)).First());

        foreach (var reply in topReplies.Values)
            authorIds.Add(reply.AuthorUserId);

        var presentations = await _presentation.GetPresentationsAsync(authorIds.Distinct(), viewerUserId, forumId, ct);
        var viewerUpvotes = await _upvotes.GetViewerUpvotedPostIdsAsync(rootIds, viewerUserId, ct);
        var attachmentsByPost = await _media.GetAttachmentsByPostIdsAsync(rootIds, viewerUserId, ct);
        var reactionsByPost = await _emotes.GetReactionsByPostIdsAsync(rootIds, viewerUserId, ct);

        var results = new List<PostDto>();
        foreach (var post in posts)
        {
            TopReplyPreviewDto? topPreview = null;
            if (topReplies.TryGetValue(post.Id, out var topReply))
            {
                var previewMessage = topReply.IsDeleted ? DeletedMessage : topReply.Message;
                topPreview = new TopReplyPreviewDto(
                    topReply.Id,
                    presentations[topReply.AuthorUserId],
                    previewMessage,
                    topReply.UpvoteCount,
                    topReply.CreatedAt);
            }

            attachmentsByPost.TryGetValue(post.Id, out var attachments);
            reactionsByPost.TryGetValue(post.Id, out var reactions);
            results.Add(await ToDtoAsync(
                post,
                presentations[post.AuthorUserId],
                [],
                viewerUpvotes,
                viewerUserId,
                ct,
                attachments,
                topPreview,
                reactions));
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

        if (sort is PostSort.New or PostSort.Updated)
        {
            var query = _db.Posts
                .Include(p => p.Author)
                .Where(p => p.ForumId != null && forumIds.Contains(p.ForumId.Value) && p.ParentPostId == null && !p.IsDeleted && (!ownedOnly || p.OriginNodeId == null));

            query = sort == PostSort.Updated
                ? query.OrderByDescending(p => p.IsPinned).ThenByDescending(p => p.UpdatedAt).ThenByDescending(p => p.CreatedAt)
                : query.OrderByDescending(p => p.IsPinned).ThenByDescending(p => p.CreatedAt);

            return await query
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
            ancestor.UpdatedAt = DateTime.UtcNow;
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
        CancellationToken ct,
        Post? policyRoot = null)
    {
        var lookup = threadPosts
            .GroupBy(p => p.ParentPostId ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => g.ToList());
        var attachmentsByPost = await _media.GetAttachmentsByPostIdsAsync(threadPosts.Select(p => p.Id), viewerUserId, ct);
        var reactionsByPost = await _emotes.GetReactionsByPostIdsAsync(threadPosts.Select(p => p.Id), viewerUserId, ct);
        return await BuildNodeAsync(
            root, lookup, presentations, viewerUpvotes, viewerUserId, attachmentsByPost, reactionsByPost, policyRoot ?? root, ct);
    }

    private async Task<PostDto> BuildNodeAsync(
        Post post,
        Dictionary<Guid, List<Post>> lookup,
        IReadOnlyDictionary<Guid, UserPresentationDto> presentations,
        IReadOnlySet<Guid> viewerUpvotes,
        Guid? viewerUserId,
        IReadOnlyDictionary<Guid, IReadOnlyList<MediaAttachmentDto>> attachmentsByPost,
        IReadOnlyDictionary<Guid, IReadOnlyList<PostReactionDto>> reactionsByPost,
        Post threadRoot,
        CancellationToken ct)
    {
        var children = lookup.GetValueOrDefault(post.Id, [])
            .OrderBy(p => p.CreatedAt)
            .ToList();

        var childDtos = new List<PostDto>();
        foreach (var child in children)
        {
            childDtos.Add(await BuildNodeAsync(
                child, lookup, presentations, viewerUpvotes, viewerUserId, attachmentsByPost, reactionsByPost, threadRoot, ct));
        }

        attachmentsByPost.TryGetValue(post.Id, out var attachments);
        reactionsByPost.TryGetValue(post.Id, out var reactions);
        return await ToDtoAsync(
            post, presentations[post.AuthorUserId], childDtos, viewerUpvotes, viewerUserId, ct, attachments, reactions: reactions, threadRoot: threadRoot);
    }

    private async Task TryRecordReplyAlertAsync(Post post, CancellationToken ct)
    {
        try
        {
            await _replyAlerts.RecordReplyAsync(post, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record reply alert for post {PostId}", post.Id);
        }
    }

    private async Task TryIndexPostAsync(Post post, CancellationToken ct)
    {
        if (!_searchIndex.IsEnabled) return;
        try
        {
            var username = post.Author?.Username;
            if (string.IsNullOrEmpty(username))
            {
                username = await _db.Users.AsNoTracking()
                    .Where(u => u.Id == post.AuthorUserId)
                    .Select(u => u.Username)
                    .FirstOrDefaultAsync(ct) ?? "";
            }

            var rootId = await GetThreadRootIdAsync(post.Id, ct);
            await _searchIndex.IndexAsync(PostSearchDocuments.From(post, rootId, username), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenSearch index failed for post {PostId}", post.Id);
        }
    }

    private async Task TryDeleteFromIndexAsync(Guid postId, CancellationToken ct)
    {
        if (!_searchIndex.IsEnabled) return;
        try
        {
            await _searchIndex.DeleteAsync(postId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenSearch delete failed for post {PostId}", postId);
        }
    }

    private static IReadOnlyList<string> TokenizeSearchQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        return query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Length > 64 ? token[..64] : token)
            .Take(8)
            .ToList();
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "", StringComparison.Ordinal)
            .Replace("%", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal);

    private static string MakeSnippet(string text, IReadOnlyList<string> tokens, int max = 180)
    {
        var normalized = text.ReplaceLineEndings(" ").Trim();
        if (normalized.Length == 0) return "";
        var index = -1;
        foreach (var token in tokens)
        {
            index = normalized.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index >= 0) break;
        }

        if (index < 0)
            return normalized.Length <= max ? normalized : normalized[..max] + "…";

        var start = Math.Max(0, index - 40);
        var length = Math.Min(max, normalized.Length - start);
        var slice = normalized.Substring(start, length);
        if (start > 0) slice = "…" + slice;
        if (start + length < normalized.Length) slice += "…";
        return slice;
    }

    private static Guid? RootIdFromLoaded(Post post, IReadOnlyDictionary<Guid, Post> byId)
    {
        var current = post;
        var guard = 0;
        while (current.ParentPostId.HasValue && guard++ < 32)
        {
            if (!byId.TryGetValue(current.ParentPostId.Value, out var parent))
                return current.ParentPostId;
            current = parent;
        }

        return current.ParentPostId.HasValue ? current.ParentPostId : current.Id;
    }

    private async Task<Guid> ResolveThreadRootIdAsync(Guid postId, CancellationToken ct)
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
        Post threadRoot,
        Guid? viewerUserId,
        CancellationToken ct)
    {
        if (!viewerUserId.HasValue || !post.IsLocallyOwned) return null;

        var perms = post.WatercoolerId.HasValue
            ? default(ForumPermissions)
            : await _permissions.GetForumPermissionsAsync(viewerUserId, post.ForumId!.Value, ct);
        var coolerPerms = post.WatercoolerId.HasValue
            ? await _permissions.GetWatercoolerPermissionsAsync(viewerUserId, post.WatercoolerId.Value, ct)
            : WatercoolerPermissions.None;
        var isAuthor = post.AuthorUserId == viewerUserId.Value;
        var isRoot = !post.ParentPostId.HasValue;
        var canManage = post.WatercoolerId.HasValue
            ? coolerPerms.HasFlag(WatercoolerPermissions.ManageWatercooler)
            : perms.HasFlag(ForumPermissions.ManagePosts);
        var canEdit = !post.IsDeleted &&
                      (isAuthor && (post.WatercoolerId.HasValue || perms.HasFlag(ForumPermissions.EditOwnPost)) || canManage);
        var canSoftDelete = !post.IsDeleted &&
                            (isAuthor && (post.WatercoolerId.HasValue || perms.HasFlag(ForumPermissions.DeleteOwnPost)) || canManage);
        var canHardDelete = post.IsDeleted && canManage;
        var hasHistory = await _db.PostRevisions.AnyAsync(r => r.PostId == post.Id, ct);
        var canViewHistory = hasHistory && (isAuthor ||
            (post.WatercoolerId.HasValue
                ? coolerPerms.HasFlag(WatercoolerPermissions.ViewHistory)
                : perms.HasFlag(ForumPermissions.ViewHistory)));
        var canLock = isRoot && canManage && !post.IsLocked;
        var canUnlock = isRoot && canManage && post.IsLocked;
        var canPin = isRoot && canManage && !post.IsPinned;
        var canUnpin = isRoot && canManage && post.IsPinned;
        var canReport = !isAuthor && !post.IsDeleted &&
                        (post.WatercoolerId.HasValue
                            ? coolerPerms.HasFlag(WatercoolerPermissions.ViewWatercooler)
                            : perms.HasFlag(ForumPermissions.ViewForum));

        var canMarkNsfwMedia = !post.IsDeleted && post.ForumId.HasValue && !post.WatercoolerId.HasValue &&
                              (isAuthor || perms.HasFlag(ForumPermissions.MarkNsfwMedia));
        var canSetReplyPolicy = isRoot && (isAuthor || canManage);
        var canReply = await ViewerCanReplyAsync(threadRoot, viewerUserId.Value, canManage, ct);

        return new PostViewerActionsDto(
            canEdit, canSoftDelete, canHardDelete, canViewHistory,
            canLock, canUnlock, canPin, canUnpin, canReport, canMarkNsfwMedia, canReply, canSetReplyPolicy,
            CanUpvote: !isAuthor);
    }

    private async Task<PostDto> ToDtoAsync(
        Post post,
        UserPresentationDto author,
        List<PostDto> replies,
        IReadOnlySet<Guid> viewerUpvotes,
        Guid? viewerUserId,
        CancellationToken ct,
        IReadOnlyList<MediaAttachmentDto>? attachments = null,
        TopReplyPreviewDto? topReplyPreview = null,
        IReadOnlyList<PostReactionDto>? reactions = null,
        Post? threadRoot = null)
    {
        var actions = await BuildViewerActionsAsync(post, threadRoot ?? post, viewerUserId, ct);
        if (attachments is null)
        {
            var map = await _media.GetAttachmentsByPostIdsAsync([post.Id], viewerUserId, ct);
            attachments = map.TryGetValue(post.Id, out var list) ? list : [];
        }

        if (reactions is null)
        {
            var map = await _emotes.GetReactionsByPostIdsAsync([post.Id], viewerUserId, ct);
            reactions = map.TryGetValue(post.Id, out var list) ? list : [];
        }

        var quoted = await MapQuotedAsync(post, viewerUserId, ct);
        return ToDto(post, author, replies, viewerUpvotes, actions, attachments, topReplyPreview, reactions, quoted);
    }

    private async Task<QuotedPostDto?> MapQuotedAsync(Post post, Guid? viewerUserId, CancellationToken ct)
    {
        if (!post.QuotedPostId.HasValue) return null;
        var quoted = await _db.Posts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == post.QuotedPostId.Value, ct);
        if (quoted is null || !quoted.WatercoolerId.HasValue) return null;
        var canView = await _permissions.CanViewWatercoolerAsync(viewerUserId, quoted.WatercoolerId.Value, ct);
        var author = await _presentation.GetPresentationAsync(quoted.AuthorUserId, viewerUserId, null, ct);
        var body = canView ? (quoted.IsDeleted ? DeletedMessage : quoted.Message) : "";
        return new QuotedPostDto(quoted.Id, author, body, quoted.WatercoolerId.Value, quoted.CreatedAt, canView);
    }

    private async Task<PostDto> BuildConversationAsync(
        Post focused,
        List<Post> threadPosts,
        IReadOnlyDictionary<Guid, UserPresentationDto> presentations,
        IReadOnlySet<Guid> viewerUpvotes,
        Guid? viewerUserId,
        CancellationToken ct)
    {
        var byId = threadPosts.ToDictionary(p => p.Id);
        var ancestors = new List<Post>();
        var current = focused;
        var guard = 0;
        while (current.ParentPostId.HasValue && guard++ < 32)
        {
            if (!byId.TryGetValue(current.ParentPostId.Value, out var parent)) break;
            ancestors.Insert(0, parent);
            current = parent;
        }

        var tree = await BuildTreeAsync(focused, threadPosts, presentations, viewerUpvotes, viewerUserId, ct, policyRoot: current);
        var ancestorDtos = new List<PostDto>();
        foreach (var ancestor in ancestors)
        {
            ancestorDtos.Add(await ToDtoAsync(
                ancestor,
                presentations[ancestor.AuthorUserId],
                [],
                viewerUpvotes,
                viewerUserId,
                ct,
                threadRoot: current));
        }

        return tree with { Ancestors = ancestorDtos };
    }

    private async Task<bool> CanViewHistoryAsync(Guid userId, Post post, CancellationToken ct)
    {
        if (post.WatercoolerId.HasValue)
        {
            var perms = await _permissions.GetWatercoolerPermissionsAsync(userId, post.WatercoolerId.Value, ct);
            return perms.HasFlag(WatercoolerPermissions.ViewHistory);
        }

        if (!post.ForumId.HasValue) return false;
        var forumPerms = await _permissions.GetForumPermissionsAsync(userId, post.ForumId.Value, ct);
        return forumPerms.HasFlag(ForumPermissions.ViewHistory);
    }

    private async Task<bool> CanModerateAsync(Guid userId, Post post, CancellationToken ct)
    {
        if (post.WatercoolerId.HasValue)
        {
            var perms = await _permissions.GetWatercoolerPermissionsAsync(userId, post.WatercoolerId.Value, ct);
            return perms.HasFlag(WatercoolerPermissions.ManageWatercooler);
        }

        if (!post.ForumId.HasValue) return false;
        var forumPerms = await _permissions.GetForumPermissionsAsync(userId, post.ForumId.Value, ct);
        return forumPerms.HasFlag(ForumPermissions.ManagePosts);
    }

    private async Task RequireModerateAsync(Guid userId, Post post, CancellationToken ct)
    {
        if (await CanModerateAsync(userId, post, ct)) return;
        throw new PermissionDeniedException(
            post.WatercoolerId.HasValue
                ? nameof(WatercoolerPermissions.ManageWatercooler)
                : nameof(ForumPermissions.ManagePosts),
            post.WatercoolerId.HasValue ? "watercooler" : "forum");
    }

    private async Task<bool> CanModerateOrEditOwnAsync(Guid userId, Post post, bool edit, CancellationToken ct)
    {
        if (await CanModerateAsync(userId, post, ct)) return true;
        if (post.AuthorUserId != userId) return false;
        if (post.WatercoolerId.HasValue) return true;
        if (!post.ForumId.HasValue) return false;
        var perms = await _permissions.GetForumPermissionsAsync(userId, post.ForumId.Value, ct);
        return edit ? perms.HasFlag(ForumPermissions.EditOwnPost) : perms.HasFlag(ForumPermissions.DeleteOwnPost);
    }

    public static PostDto ToDto(
        Post post,
        UserPresentationDto author,
        List<PostDto> replies,
        IReadOnlySet<Guid> viewerUpvotes,
        PostViewerActionsDto? viewerActions = null,
        IReadOnlyList<MediaAttachmentDto>? attachments = null,
        TopReplyPreviewDto? topReplyPreview = null,
        IReadOnlyList<PostReactionDto>? reactions = null,
        QuotedPostDto? quotedPost = null,
        IReadOnlyList<PostDto>? ancestors = null) => new(
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
        viewerActions,
        post.Title,
        attachments ?? [],
        topReplyPreview,
        reactions ?? [],
        post.WatercoolerId,
        post.QuotedPostId,
        post.QuoteCount,
        quotedPost,
        ancestors,
        string.IsNullOrWhiteSpace(post.ReplyPolicy) ? ReplyPolicies.Everyone : post.ReplyPolicy);

    private async Task EnsureCanReplyUnderPolicyAsync(Post? root, Guid userId, CancellationToken ct)
    {
        if (root is null) return;
        var canManage = await CanModerateAsync(userId, root, ct);
        if (await ViewerCanReplyAsync(root, userId, canManage, ct))
            return;

        throw new InvalidOperationException(
            "The original poster limited replies to people they follow.");
    }

    private async Task<bool> ViewerCanReplyAsync(Post root, Guid viewerUserId, bool canManage, CancellationToken ct)
    {
        if (root.IsLocked && !canManage)
            return false;

        if (root.AuthorUserId == viewerUserId || canManage)
            return true;

        if (!ReplyPolicies.IsFollowingOnly(root.ReplyPolicy))
            return true;

        var opDid = root.AuthorDid;
        string? issuerUrl = null;
        if (string.IsNullOrWhiteSpace(opDid))
        {
            var op = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == root.AuthorUserId, ct);
            opDid = op?.Did;
            issuerUrl = op?.AuthIssuerUrl;
        }
        else
        {
            issuerUrl = await _db.Users.AsNoTracking()
                .Where(u => u.Id == root.AuthorUserId)
                .Select(u => u.AuthIssuerUrl)
                .FirstOrDefaultAsync(ct);
        }

        var viewerDid = await _db.Users.AsNoTracking()
            .Where(u => u.Id == viewerUserId)
            .Select(u => u.Did)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(opDid) || string.IsNullOrWhiteSpace(viewerDid))
            return false;

        if (string.Equals(opDid, viewerDid, StringComparison.Ordinal))
            return true;

        if (string.IsNullOrWhiteSpace(issuerUrl))
        {
            issuerUrl = (await _db.EffectiveTrustedIssuersAsync(ct))
                .OrderBy(i => i.Name, StringComparer.Ordinal)
                .Select(i => i.IssuerUrl)
                .FirstOrDefault();
        }

        return await _followGraph.FollowsAsync(issuerUrl, opDid, viewerDid, ct);
    }
}
