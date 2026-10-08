using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Snekl.Hub.Domain;
using Snekl.Hub.Domain.Dtos;
using Snekl.Hub.Domain.Entities;
using Snekl.Hub.Domain.Ranking;
using Snekl.Hub.Infrastructure.Data;
using StackExchange.Redis;

namespace Snekl.Hub.Infrastructure.Services;

public interface IHubFeedService
{
    Task<IReadOnlyList<HubFeedCardDto>> GetFeedAsync(
        string sort = "hot",
        int limit = 50,
        string? viewerDid = null,
        int offset = 0,
        CancellationToken ct = default);

    Task<IReadOnlyList<HubSearchHitDto>> SearchAsync(
        string? query,
        int limit = 20,
        string? viewerDid = null,
        int offset = 0,
        CancellationToken ct = default);

    Task IngestCardAsync(HubFeedCardDto card, CancellationToken ct = default);
    Task IngestScoresAsync(IReadOnlyList<HubScorePatchDto> patches, CancellationToken ct = default);
    Task IngestMembershipAsync(HubForumMembershipDto payload, CancellationToken ct = default);
    Task ReindexAsync(CancellationToken ct = default);
}

public class HubFeedService : IHubFeedService
{
    public const string VisibilityPublic = "public";
    public const string VisibilityMembers = "members";
    public const string EventUpsert = "upsert";
    public const string EventDelete = "delete";

    private const string RedisCardPrefix = "hub:card:";
    private const string RedisPublicHot = "hub:z:public:hot";
    private const string RedisPublicNew = "hub:z:public:new";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HubDbContext _db;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<HubFeedService> _logger;

    public HubFeedService(
        HubDbContext db,
        ILogger<HubFeedService> logger,
        IServiceProvider services)
    {
        _db = db;
        _logger = logger;
        _redis = services.GetService<IConnectionMultiplexer>();
    }

    public async Task<IReadOnlyList<HubFeedCardDto>> GetFeedAsync(
        string sort = "hot",
        int limit = 50,
        string? viewerDid = null,
        int offset = 0,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 100);
        offset = Math.Max(0, offset);
        var newestFirst = string.Equals(sort, "new", StringComparison.OrdinalIgnoreCase);
        var activeDidList = await ActiveServerDidsAsync(ct);
        var activeDids = activeDidList.ToHashSet(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(viewerDid) && _redis is not null)
        {
            var fromRedis = await TryReadRedisAsync(newestFirst, offset, limit, activeDids, ct);
            if (fromRedis is not null)
                return fromRedis;
        }

        var cards = await LoadVisibleCardsAsync(viewerDid, activeDidList, ct);

        IEnumerable<FeedCard> ordered = newestFirst
            ? cards.OrderByDescending(c => c.CreatedAt)
            : cards.OrderByDescending(c => PostRanking.HotScore(c.Score, c.CreatedAt));

        return ordered.Skip(offset).Take(limit).Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<HubSearchHitDto>> SearchAsync(
        string? query,
        int limit = 20,
        string? viewerDid = null,
        int offset = 0,
        CancellationToken ct = default)
    {
        var tokens = TokenizeSearchQuery(query);
        if (tokens.Count == 0) return [];

        limit = Math.Clamp(limit <= 0 ? 20 : limit, 1, 50);
        offset = Math.Max(0, offset);
        var activeDidList = await ActiveServerDidsAsync(ct);
        var cards = await LoadVisibleCardsAsync(viewerDid, activeDidList, ct);
        var hits = new List<HubSearchHitDto>();
        foreach (var card in cards.OrderByDescending(c => c.CreatedAt))
        {
            var haystack = $"{card.Title}\n{card.Excerpt}\n{card.AuthorDisplayName}\n{card.ForumName}";
            if (!tokens.All(token => haystack.Contains(token, StringComparison.OrdinalIgnoreCase)))
                continue;
            hits.Add(new HubSearchHitDto(ToDto(card), MakeSnippet(haystack, tokens)));
        }

        return hits.Skip(offset).Take(limit).ToList();
    }

    public async Task IngestCardAsync(HubFeedCardDto card, CancellationToken ct = default)
    {
        var serverDid = (card.ServerDid ?? "").Trim();
        if (string.IsNullOrWhiteSpace(serverDid) || card.PostId == Guid.Empty)
            throw new InvalidOperationException("ServerDid and PostId are required.");

        var server = await FindActiveServerAsync(serverDid, ct)
            ?? throw new InvalidOperationException("Server is not an active federation member.");

        var canonicalDid = server.ServerDid;
        var isDelete = string.Equals(card.Event, EventDelete, StringComparison.OrdinalIgnoreCase);
        var eventAt = NormalizeUtc(card.EventAt);

        var existing = await FindCardAsync(canonicalDid, card.PostId, ct);
        if (existing is not null && eventAt < existing.EventAt)
            return;

        if (existing is null)
        {
            existing = new FeedCard
            {
                ServerDid = canonicalDid,
                PostId = card.PostId
            };
            _db.FeedCards.Add(existing);
        }

        existing.EventAt = eventAt;
        existing.IsDeleted = isDelete;
        if (!isDelete)
            ApplyCardBody(existing, card, canonicalDid, server.BaseUrl);

        await _db.SaveChangesAsync(ct);
        await IndexAsync(existing);
    }

    public async Task IngestScoresAsync(IReadOnlyList<HubScorePatchDto> patches, CancellationToken ct = default)
    {
        foreach (var patch in patches)
        {
            var serverDid = (patch.ServerDid ?? "").Trim();
            if (string.IsNullOrWhiteSpace(serverDid) || patch.PostId == Guid.Empty)
                continue;

            var server = await FindActiveServerAsync(serverDid, ct);
            if (server is null)
                continue;

            var card = await FindCardAsync(server.ServerDid, patch.PostId, ct);
            if (card is null || card.IsDeleted)
                continue;

            card.Score = patch.Score;
            card.CommentCount = patch.CommentCount;
            await _db.SaveChangesAsync(ct);
            await IndexAsync(card);
        }
    }

    public async Task IngestMembershipAsync(HubForumMembershipDto payload, CancellationToken ct = default)
    {
        var userDid = (payload.UserDid ?? "").Trim();
        var serverDid = (payload.ServerDid ?? "").Trim();
        if (string.IsNullOrWhiteSpace(userDid) || string.IsNullOrWhiteSpace(serverDid) || payload.ForumId == Guid.Empty)
            throw new InvalidOperationException("UserDid, ServerDid, and ForumId are required.");

        var server = await FindActiveServerAsync(serverDid, ct)
            ?? throw new InvalidOperationException("Server is not an active federation member.");

        var userKeys = FederationRegistry.DidLookupKeys(userDid);
        var serverKeys = FederationRegistry.DidLookupKeys(server.ServerDid);
        var existing = await _db.ForumMemberships.FirstOrDefaultAsync(
            m => m.ForumId == payload.ForumId && userKeys.Contains(m.UserDid) && serverKeys.Contains(m.ServerDid),
            ct);

        var isDelete = string.Equals(payload.Event, EventDelete, StringComparison.OrdinalIgnoreCase);
        if (isDelete)
        {
            if (existing is not null)
            {
                _db.ForumMemberships.Remove(existing);
                await _db.SaveChangesAsync(ct);
            }
            return;
        }

        if (existing is null)
        {
            _db.ForumMemberships.Add(new ForumMembership
            {
                UserDid = userDid,
                ServerDid = server.ServerDid,
                ForumId = payload.ForumId,
                IsFavorite = payload.IsFavorite,
                UpdatedAt = NormalizeUtc(payload.EventAt == default ? DateTime.UtcNow : payload.EventAt)
            });
        }
        else
        {
            existing.IsFavorite = payload.IsFavorite;
            existing.UpdatedAt = NormalizeUtc(payload.EventAt == default ? DateTime.UtcNow : payload.EventAt);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task ReindexAsync(CancellationToken ct = default)
    {
        if (_redis is null) return;
        var cards = await _db.FeedCards.AsNoTracking().ToListAsync(ct);
        foreach (var card in cards)
            await IndexAsync(card);
    }

    private async Task<List<string>> ActiveServerDidsAsync(CancellationToken ct) =>
        await _db.RegisteredServers.AsNoTracking()
            .Where(s => s.Status == FederationMembership.StatusActive)
            .Select(s => s.ServerDid)
            .ToListAsync(ct);

    private async Task<List<FeedCard>> LoadVisibleCardsAsync(
        string? viewerDid,
        IReadOnlyList<string> activeDidList,
        CancellationToken ct)
    {
        var cards = await _db.FeedCards.AsNoTracking()
            .Where(c => !c.IsDeleted && c.Visibility == VisibilityPublic && activeDidList.Contains(c.ServerDid))
            .ToListAsync(ct);

        if (string.IsNullOrWhiteSpace(viewerDid)) return cards;

        var memberCards = await LoadMemberCardsAsync(viewerDid, activeDidList, ct);
        if (memberCards.Count == 0) return cards;

        var seen = cards.Select(c => (c.ServerDid, c.PostId)).ToHashSet();
        foreach (var card in memberCards)
        {
            if (seen.Add((card.ServerDid, card.PostId)))
                cards.Add(card);
        }

        return cards;
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

    private async Task<List<FeedCard>> LoadMemberCardsAsync(
        string viewerDid,
        IReadOnlyList<string> activeDidList,
        CancellationToken ct)
    {
        var userKeys = FederationRegistry.DidLookupKeys(viewerDid);
        var memberships = await _db.ForumMemberships.AsNoTracking()
            .Where(m => userKeys.Contains(m.UserDid))
            .ToListAsync(ct);
        if (memberships.Count == 0)
            return [];

        var forumIds = memberships.Select(m => m.ForumId).Distinct().ToList();
        var serverKeys = memberships
            .SelectMany(m => FederationRegistry.DidLookupKeys(m.ServerDid))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var candidates = await _db.FeedCards.AsNoTracking()
            .Where(c =>
                !c.IsDeleted &&
                c.Visibility == VisibilityMembers &&
                forumIds.Contains(c.ForumId) &&
                serverKeys.Contains(c.ServerDid) &&
                activeDidList.Contains(c.ServerDid))
            .ToListAsync(ct);

        return candidates
            .Where(card => memberships.Any(m =>
                m.ForumId == card.ForumId &&
                FederationRegistry.DidLookupKeys(m.ServerDid).Contains(card.ServerDid)))
            .ToList();
    }

    private async Task<RegisteredServer?> FindActiveServerAsync(string serverDid, CancellationToken ct)
    {
        var keys = FederationRegistry.DidLookupKeys(serverDid);
        return await _db.RegisteredServers
            .FirstOrDefaultAsync(
                s => s.Status == FederationMembership.StatusActive && keys.Contains(s.ServerDid),
                ct);
    }

    private async Task<FeedCard?> FindCardAsync(string serverDid, Guid postId, CancellationToken ct)
    {
        var keys = FederationRegistry.DidLookupKeys(serverDid);
        return await _db.FeedCards.FirstOrDefaultAsync(
            c => c.PostId == postId && keys.Contains(c.ServerDid),
            ct);
    }

    private static void ApplyCardBody(FeedCard row, HubFeedCardDto card, string serverDid, string originBaseUrl)
    {
        var visibility = (card.Visibility ?? "").Trim().ToLowerInvariant();
        if (visibility is not VisibilityPublic and not VisibilityMembers)
            visibility = VisibilityMembers;

        row.ServerDid = serverDid;
        row.PostId = card.PostId;
        row.ForumId = card.ForumId;
        row.ForumSlug = (card.ForumSlug ?? "").Trim();
        row.ForumName = string.IsNullOrWhiteSpace(card.ForumName) ? row.ForumSlug : card.ForumName.Trim();
        row.OriginBaseUrl = string.IsNullOrWhiteSpace(card.OriginBaseUrl)
            ? originBaseUrl.TrimEnd('/')
            : card.OriginBaseUrl.Trim().TrimEnd('/');
        row.OriginPostUrl = (card.OriginPostUrl ?? "").Trim();
        row.Title = string.IsNullOrWhiteSpace(card.Title) ? null : card.Title.Trim();
        row.Excerpt = card.Excerpt ?? "";
        row.ImagePreviewUrl = string.IsNullOrWhiteSpace(card.ImagePreviewUrl) ? null : card.ImagePreviewUrl.Trim();
        row.NsfwCover = string.IsNullOrWhiteSpace(card.NsfwCover) ? null : card.NsfwCover.Trim();
        if (row.NsfwCover is { Length: > 140 })
            row.NsfwCover = row.NsfwCover[..140].Trim();
        row.AuthorDid = card.AuthorDid ?? "";
        row.AuthorDisplayName = string.IsNullOrWhiteSpace(card.AuthorDisplayName)
            ? card.AuthorDid ?? ""
            : card.AuthorDisplayName.Trim();
        row.CreatedAt = NormalizeUtc(card.CreatedAt);
        row.EditedAt = card.EditedAt.HasValue ? NormalizeUtc(card.EditedAt.Value) : null;
        row.Visibility = visibility;
        row.Score = card.Score;
        row.CommentCount = card.CommentCount;
        row.MapVersion = card.MapVersion;
        row.SchemaVersion = card.SchemaVersion <= 0 ? 1 : card.SchemaVersion;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static HubFeedCardDto ToDto(FeedCard card) =>
        new(
            card.SchemaVersion,
            card.PostId,
            card.ForumId,
            card.ForumSlug,
            card.ForumName,
            card.ServerDid,
            card.OriginBaseUrl,
            card.OriginPostUrl,
            card.Title,
            card.Excerpt,
            card.ImagePreviewUrl,
            card.AuthorDid,
            card.AuthorDisplayName,
            card.CreatedAt,
            card.EditedAt,
            card.Visibility,
            card.Score,
            card.CommentCount,
            card.MapVersion,
            card.IsDeleted ? EventDelete : EventUpsert,
            card.EventAt,
            card.NsfwCover);

    private async Task IndexAsync(FeedCard card)
    {
        if (_redis is null) return;
        try
        {
            var db = _redis.GetDatabase();
            var member = RedisMember(card.ServerDid, card.PostId);
            var cardKey = RedisCardPrefix + member;
            var publicIndex = !card.IsDeleted && card.Visibility == VisibilityPublic;
            if (publicIndex)
            {
                var dto = ToDto(card);
                await db.StringSetAsync(cardKey, JsonSerializer.Serialize(dto, JsonOptions));
                await db.SortedSetAddAsync(RedisPublicHot, member, PostRanking.HotScore(card.Score, card.CreatedAt));
                await db.SortedSetAddAsync(
                    RedisPublicNew,
                    member,
                    new DateTimeOffset(card.CreatedAt).ToUnixTimeSeconds());
            }
            else
            {
                await db.KeyDeleteAsync(cardKey);
                await db.SortedSetRemoveAsync(RedisPublicHot, member);
                await db.SortedSetRemoveAsync(RedisPublicNew, member);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hub Redis feed index update failed for {ServerDid}/{PostId}", card.ServerDid, card.PostId);
        }
    }

    private async Task<IReadOnlyList<HubFeedCardDto>?> TryReadRedisAsync(
        bool newestFirst,
        int offset,
        int limit,
        HashSet<string> activeDids,
        CancellationToken ct)
    {
        try
        {
            var db = _redis!.GetDatabase();
            var key = newestFirst ? RedisPublicNew : RedisPublicHot;
            var members = await db.SortedSetRangeByRankAsync(key, offset, offset + limit - 1, Order.Descending);
            if (members.Length == 0)
                return null;

            var ranked = new List<(string ServerDid, Guid PostId)>();
            foreach (var member in members)
            {
                if (!TryParseMember(member.ToString(), out var serverDid, out var postId))
                    continue;
                if (!activeDids.Contains(serverDid))
                    continue;
                ranked.Add((serverDid, postId));
            }

            if (ranked.Count == 0)
                return null;

            var postIds = ranked.Select(x => x.PostId).Distinct().ToList();
            var serverDids = ranked.Select(x => x.ServerDid).Distinct().ToList();
            var rows = await _db.FeedCards.AsNoTracking()
                .Where(c =>
                    !c.IsDeleted
                    && c.Visibility == VisibilityPublic
                    && postIds.Contains(c.PostId)
                    && serverDids.Contains(c.ServerDid))
                .ToListAsync(ct);
            var byKey = rows.ToDictionary(c => (c.ServerDid, c.PostId));
            var cards = new List<HubFeedCardDto>();
            foreach (var item in ranked)
            {
                if (byKey.TryGetValue((item.ServerDid, item.PostId), out var row))
                    cards.Add(ToDto(row));
            }

            return cards.Count == 0 ? null : cards;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hub Redis feed read failed; falling back to Postgres");
            return null;
        }
    }

    private static bool TryParseMember(string? member, out string serverDid, out Guid postId)
    {
        serverDid = "";
        postId = Guid.Empty;
        if (string.IsNullOrEmpty(member))
            return false;
        var tab = member.IndexOf('\t');
        if (tab <= 0 || tab == member.Length - 1)
            return false;
        serverDid = member[..tab];
        return Guid.TryParse(member[(tab + 1)..], out postId);
    }

    private static string RedisMember(string serverDid, Guid postId) => $"{serverDid}\t{postId:D}";
}
