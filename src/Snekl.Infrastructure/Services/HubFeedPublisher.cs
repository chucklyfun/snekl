using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Snekl.Domain.Entities;
using Snekl.Domain.Security;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IHubFeedPublisher
{
    Task PublishThreadCardAsync(Post post, bool deleted = false, CancellationToken ct = default);
    Task PublishThreadScoreAsync(Post threadRoot, CancellationToken ct = default);
    Task PublishForumMembershipAsync(
        string userDid,
        Guid forumId,
        bool deleted = false,
        bool isFavorite = false,
        CancellationToken ct = default);
    Task PublishUserMembershipsAsync(Guid userId, CancellationToken ct = default);
}

public class HubFeedPublisher : IHubFeedPublisher
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly SneklDbContext _db;
    private readonly INodeService _node;
    private readonly IPermissionService _permissions;
    private readonly IMediaService _media;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<HubFeedPublisher> _logger;

    public HubFeedPublisher(
        SneklDbContext db,
        INodeService node,
        IPermissionService permissions,
        IMediaService media,
        IHttpClientFactory http,
        ILogger<HubFeedPublisher> logger)
    {
        _db = db;
        _node = node;
        _permissions = permissions;
        _media = media;
        _http = http;
        _logger = logger;
    }

    public async Task PublishThreadCardAsync(Post post, bool deleted = false, CancellationToken ct = default)
    {
        try
        {
            if (post.ParentPostId is not null) return;
            var hubs = await ListAcceptedHubsAsync(ct);
            if (hubs.Count == 0) return;

            var node = await _node.GetOrCreateAsync(ct);
            if (post.ForumId is null) return;
            var forum = post.Forum ?? await _db.Forums.AsNoTracking().FirstOrDefaultAsync(f => f.Id == post.ForumId.Value, ct);
            if (forum is null) return;

            var author = post.Author;
            var isPublic = await _permissions.CanViewForumAsync(null, post.ForumId.Value, ct);
            HubMediaPreview? preview = deleted ? null : await _media.GetHubPreviewForPostAsync(post.Id, ct);
            var originBaseUrl = node.BaseUrl.TrimEnd('/');
            var now = DateTime.UtcNow;

            foreach (var hubUrl in hubs)
            {
                var mapVersion = await ReadMapVersionAsync(hubUrl, ct);
                var payload = new HubFeedCardPayload(
                    1,
                    post.Id,
                    post.ForumId.Value,
                    forum.Slug,
                    forum.Name,
                    node.Did,
                    originBaseUrl,
                    HubFeedCards.OriginPostUrl(originBaseUrl, forum.Slug, post.Id),
                    post.Title,
                    HubFeedCards.Excerpt(post.Message),
                    preview?.ImagePreviewUrl,
                    post.AuthorDid ?? author?.Did ?? "",
                    author?.DisplayName ?? author?.Username ?? "",
                    post.CreatedAt,
                    post.EditedAt,
                    isPublic ? HubFeedCards.VisibilityPublic : HubFeedCards.VisibilityMembers,
                    post.UpvoteCount,
                    post.ReplyCount,
                    mapVersion,
                    deleted ? HubFeedCards.EventDelete : HubFeedCards.EventUpsert,
                    now,
                    preview?.NsfwCover);

                await EnqueueAsync(hubUrl, "/api/hub/cards", HubIngestTypes.Card, payload, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hub card enqueue failed for post {PostId}", post.Id);
        }
    }

    public async Task PublishThreadScoreAsync(Post threadRoot, CancellationToken ct = default)
    {
        try
        {
            if (threadRoot.ParentPostId is not null) return;
            var hubs = await ListAcceptedHubsAsync(ct);
            if (hubs.Count == 0) return;

            var node = await _node.GetOrCreateAsync(ct);
            var patches = new[]
            {
                new HubScorePatchPayload(
                    1,
                    threadRoot.Id,
                    node.Did,
                    threadRoot.UpvoteCount,
                    threadRoot.ReplyCount,
                    DateTime.UtcNow)
            };

            foreach (var hubUrl in hubs)
                await EnqueueAsync(hubUrl, "/api/hub/scores", HubIngestTypes.Scores, patches, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hub score enqueue failed for post {PostId}", threadRoot.Id);
        }
    }

    public async Task PublishForumMembershipAsync(
        string userDid,
        Guid forumId,
        bool deleted = false,
        bool isFavorite = false,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userDid) || forumId == Guid.Empty)
                return;

            var hubs = await ListAcceptedHubsAsync(ct);
            if (hubs.Count == 0) return;

            var node = await _node.GetOrCreateAsync(ct);
            var payload = new HubForumMembershipPayload(
                1,
                userDid.Trim(),
                node.Did,
                forumId,
                deleted ? "delete" : "upsert",
                DateTime.UtcNow,
                isFavorite);

            foreach (var hubUrl in hubs)
                await EnqueueAsync(hubUrl, "/api/hub/memberships", HubIngestTypes.Membership, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hub membership enqueue failed for nest {ForumId}", forumId);
        }
    }

    public async Task PublishUserMembershipsAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            var hubs = await ListAcceptedHubsAsync(ct);
            if (hubs.Count == 0) return;

            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (string.IsNullOrWhiteSpace(user?.Did)) return;

            var node = await _node.GetOrCreateAsync(ct);
            var rows = await _db.ForumMemberships.AsNoTracking()
                .Where(m => m.UserId == userId)
                .ToListAsync(ct);
            var payload = rows.Select(m => new HubForumMembershipPayload(
                1,
                user.Did,
                node.Did,
                m.ForumId,
                "upsert",
                DateTime.UtcNow,
                m.IsFavorite)).ToList();

            foreach (var hubUrl in hubs)
                await EnqueueAsync(hubUrl, "/api/hub/memberships/sync", HubIngestTypes.MembershipSync, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hub membership sync enqueue failed for user {UserId}", userId);
        }
    }

    private async Task EnqueueAsync<T>(string hubUrl, string path, string envelopeType, T payload, CancellationToken ct)
    {
        _db.HubOutbox.Add(new HubOutboxMessage
        {
            HubBaseUrl = hubUrl,
            Path = path,
            EnvelopeType = envelopeType,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            NextAttemptAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<string>> ListAcceptedHubsAsync(CancellationToken ct)
    {
        // Includes the host's hubs for a spawned server that inherits them.
        var urls = await _db.EffectiveAcceptedHubUrlsAsync(ct);
        return urls
            .Select(u => u.Trim().TrimEnd('/'))
            .Where(u => u.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<long> ReadMapVersionAsync(string hubUrl, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient();
            var info = await client.GetFromJsonAsync<HubInfoPeek>($"{hubUrl}/api/hub", JsonOptions, ct);
            return info?.MapVersion ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private sealed record HubInfoPeek(long MapVersion);

    private sealed record HubFeedCardPayload(
        int SchemaVersion,
        Guid PostId,
        Guid ForumId,
        string ForumSlug,
        string ForumName,
        string ServerDid,
        string OriginBaseUrl,
        string OriginPostUrl,
        string? Title,
        string Excerpt,
        string? ImagePreviewUrl,
        string AuthorDid,
        string AuthorDisplayName,
        DateTime CreatedAt,
        DateTime? EditedAt,
        string Visibility,
        int Score,
        int CommentCount,
        long MapVersion,
        string Event,
        DateTime EventAt,
        string? NsfwCover = null);

    private sealed record HubScorePatchPayload(
        int SchemaVersion,
        Guid PostId,
        string ServerDid,
        int Score,
        int CommentCount,
        DateTime PatchedAt);

    private sealed record HubForumMembershipPayload(
        int SchemaVersion,
        string UserDid,
        string ServerDid,
        Guid ForumId,
        string Event,
        DateTime EventAt,
        bool IsFavorite = false);
}
