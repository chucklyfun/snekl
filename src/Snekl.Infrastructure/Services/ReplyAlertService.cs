using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IReplyAlertService
{
    Task RecordReplyAsync(Post reply, CancellationToken ct = default);
    Task<IReadOnlyList<ReplyAlertDto>> ListAsync(Guid userId, int limit, CancellationToken ct = default);
    Task<int> UnreadCountAsync(Guid userId, CancellationToken ct = default);
    Task MarkReadAsync(Guid userId, Guid alertId, CancellationToken ct = default);
    Task<ReplyAlertPreferenceDto> GetPreferenceAsync(Guid userId, Guid postId, CancellationToken ct = default);
    Task<ReplyAlertPreferenceDto> SetPreferenceAsync(Guid userId, Guid postId, string mode, CancellationToken ct = default);
    Task<PlaceAlertPreferenceDto> GetPlacePreferenceAsync(Guid userId, string kind, Guid placeId, CancellationToken ct = default);
    Task<PlaceAlertPreferenceDto> SetPlacePreferenceAsync(Guid userId, string kind, Guid placeId, string mode, CancellationToken ct = default);
    Task RecordVoteMilestoneAsync(Post post, int previousCount, int count, CancellationToken ct = default);
    Task RegisterDeviceAsync(Guid userId, string platform, string token, CancellationToken ct = default);
    Task UnregisterDeviceAsync(Guid userId, string token, CancellationToken ct = default);
}

public class ReplyAlertService : IReplyAlertService
{
    private readonly SneklDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly IReplyAlertPushSender _push;
    private readonly IFollowGraphClient _follows;
    private readonly ICurrentServer? _current;
    private readonly ILogger<ReplyAlertService> _logger;

    public ReplyAlertService(
        SneklDbContext db,
        IPermissionService permissions,
        IReplyAlertPushSender push,
        ILogger<ReplyAlertService> logger,
        IFollowGraphClient follows,
        ICurrentServer? current = null)
    {
        _db = db;
        _permissions = permissions;
        _push = push;
        _logger = logger;
        _follows = follows;
        _current = current;
    }

    public async Task RecordReplyAsync(Post reply, CancellationToken ct = default)
    {
        if (reply.ParentPostId.HasValue)
            await RecordThreadReplyAsync(reply, ct);
        await RecordPlaceAsync(reply, ct);
        if (!reply.ParentPostId.HasValue)
            await RecordFollowedPostAsync(reply, ct);
    }

    public async Task RecordVoteMilestoneAsync(Post post, int previousCount, int count, CancellationToken ct = default)
    {
        var level = VoteAlertLevels.Crossed(previousCount, count, await VoteBaseAsync(ct));
        if (level is not int reached)
            return;

        var author = await _db.Users.AsNoTracking()
            .Where(u => u.Id == post.AuthorUserId)
            .Select(u => new { u.Id, u.Did, u.AuthIssuerUrl, u.DisplayName, u.Username })
            .FirstOrDefaultAsync(ct);
        if (author is null)
            return;

        var recipients = new HashSet<Guid> { author.Id };
        foreach (var userId in await WatcherUserIdsAsync(author.Did, author.AuthIssuerUrl, FollowAlertModes.WantsVotes, ct))
            recipients.Add(userId);

        var title = ThreadTitle(post.Title);
        if (string.IsNullOrWhiteSpace(post.Title) && post.ParentPostId.HasValue)
        {
            var root = await ResolveRootAsync(post, ct);
            title = ThreadTitle(root);
        }

        var now = DateTime.UtcNow;
        var serverDid = await ServerDidAsync(ct);
        var updated = new List<ReplyAlert>();
        foreach (var userId in recipients)
        {
            var already = await _db.ReplyAlerts
                .Where(a => a.RecipientUserId == userId && a.Kind == ReplyAlertKinds.Vote && a.RootPostId == post.Id)
                .Select(a => a.ReplyCount)
                .ToListAsync(ct);
            if (already.Count > 0 && already.Max() >= reached)
                continue;

            var open = await _db.ReplyAlerts.FirstOrDefaultAsync(
                a => a.RecipientUserId == userId
                    && a.Kind == ReplyAlertKinds.Vote
                    && a.RootPostId == post.Id
                    && a.ReadAt == null,
                ct);
            if (open is null)
            {
                open = new ReplyAlert
                {
                    RecipientUserId = userId,
                    Kind = ReplyAlertKinds.Vote,
                    RootPostId = post.Id,
                    SubjectUserId = author.Id,
                    ReplyCount = reached,
                    LatestReplyId = post.Id,
                    LatestReplyAt = now,
                    CreatedAt = now
                };
                _db.ReplyAlerts.Add(open);
            }
            else
            {
                open.ReplyCount = reached;
                open.LatestReplyId = post.Id;
                open.LatestReplyAt = now;
            }

            updated.Add(open);
        }

        if (updated.Count == 0)
            return;

        await _db.SaveChangesAsync(ct);
        await PushAsync(
            updated,
            alert => new ReplyAlertPush(
                alert.Id,
                post.Id,
                serverDid,
                title,
                VoteAlertBody(alert.ReplyCount, title),
                alert.ReplyCount,
                ReplyAlertKinds.Vote),
            ct);
    }

    private async Task RecordThreadReplyAsync(Post reply, CancellationToken ct)
    {
        if (reply.ParentPostId is not Guid parentPostId)
            return;

        var root = await ResolveRootAsync(reply, ct);
        var modes = await _db.ThreadAlertPreferences
            .Where(p => p.RootPostId == root.Id)
            .ToDictionaryAsync(p => p.UserId, p => p.Mode, ct);

        var recipients = new HashSet<Guid>();
        foreach (var (userId, mode) in modes)
        {
            if (mode == ReplyAlertModes.All)
                recipients.Add(userId);
        }

        var parent = await _db.Posts.FindAsync([parentPostId], ct);
        if (parent is not null)
        {
            var parentMode = modes.TryGetValue(parent.AuthorUserId, out var stored)
                ? stored
                : ReplyAlertModes.Replies;
            if (parentMode == ReplyAlertModes.Replies)
                recipients.Add(parent.AuthorUserId);
        }

        recipients.Remove(reply.AuthorUserId);
        if (recipients.Count == 0)
            return;

        var now = reply.CreatedAt == default ? DateTime.UtcNow : reply.CreatedAt;
        var title = ThreadTitle(root);
        var authorName = await AuthorNameAsync(reply.AuthorUserId, ct);
        var serverDid = await _db.NodeConfig.Select(n => n.Did).FirstOrDefaultAsync(ct) ?? "";
        var updated = new List<ReplyAlert>();

        foreach (var userId in recipients)
        {
            var open = await _db.ReplyAlerts.FirstOrDefaultAsync(
                a => a.RecipientUserId == userId
                    && a.Kind == ReplyAlertKinds.Thread
                    && a.RootPostId == root.Id
                    && a.ReadAt == null,
                ct);
            if (open is null)
            {
                open = new ReplyAlert
                {
                    RecipientUserId = userId,
                    Kind = ReplyAlertKinds.Thread,
                    RootPostId = root.Id,
                    ReplyCount = 1,
                    LatestReplyId = reply.Id,
                    LatestReplyAt = now,
                    CreatedAt = now
                };
                _db.ReplyAlerts.Add(open);
            }
            else
            {
                open.ReplyCount += 1;
                open.LatestReplyId = reply.Id;
                open.LatestReplyAt = now;
            }

            updated.Add(open);
        }

        await _db.SaveChangesAsync(ct);

        foreach (var alert in updated)
        {
            var devices = await _db.PushDevices
                .Where(d => d.UserId == alert.RecipientUserId)
                .ToListAsync(ct);
            if (devices.Count == 0)
                continue;

            var push = new ReplyAlertPush(
                alert.Id,
                root.Id,
                serverDid,
                title,
                AlertBody(alert.ReplyCount, authorName, title),
                alert.ReplyCount,
                ReplyAlertKinds.Thread);
            try
            {
                var dropped = await _push.SendAsync(devices, push, ct);
                if (dropped.Count == 0)
                    continue;
                var stale = await _db.PushDevices.Where(d => dropped.Contains(d.Token)).ToListAsync(ct);
                _db.PushDevices.RemoveRange(stale);
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reply alert push failed for {AlertId}", alert.Id);
            }
        }
    }

    public async Task<IReadOnlyList<ReplyAlertDto>> ListAsync(Guid userId, int limit, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit, 1, 100);
        var rows = await _db.ReplyAlerts
            .Where(a => a.RecipientUserId == userId)
            .OrderByDescending(a => a.LatestReplyAt)
            .ThenByDescending(a => a.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
        if (rows.Count == 0)
            return [];

        var rootIds = rows.Where(r =>
                r.RootPostId.HasValue && r.Kind is ReplyAlertKinds.Thread or ReplyAlertKinds.Vote)
            .Select(r => r.RootPostId!.Value)
            .Distinct()
            .ToList();
        var titles = await _db.Posts
            .Where(p => rootIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Title })
            .ToDictionaryAsync(p => p.Id, p => p.Title, ct);

        var forumIds = rows.Where(r => r.ForumId.HasValue).Select(r => r.ForumId!.Value).Distinct().ToList();
        var forums = await _db.Forums
            .Where(f => forumIds.Contains(f.Id))
            .Select(f => new
            {
                f.Id,
                f.Name,
                f.Slug,
                ParentSlug = f.ParentForum != null ? f.ParentForum.Slug : null
            })
            .ToDictionaryAsync(f => f.Id, ct);

        var coolerIds = rows.Where(r => r.WatercoolerId.HasValue).Select(r => r.WatercoolerId!.Value).Distinct().ToList();
        var coolers = await _db.Watercoolers
            .Where(w => coolerIds.Contains(w.Id))
            .Select(w => new
            {
                w.Id,
                w.Name,
                w.Slug,
                ParentSlug = w.ParentForum != null ? w.ParentForum.Slug : null,
                ForumParentSlug = w.ParentForum != null && w.ParentForum.ParentForum != null
                    ? w.ParentForum.ParentForum.Slug
                    : null
            })
            .ToDictionaryAsync(w => w.Id, ct);

        var subjectIds = rows.Where(r => r.Kind == ReplyAlertKinds.Follow && r.SubjectUserId.HasValue)
            .Select(r => r.SubjectUserId!.Value)
            .Distinct()
            .ToList();
        var subjects = await _db.Users
            .Where(u => subjectIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Username })
            .ToDictionaryAsync(u => u.Id, ct);

        return rows.Select(row =>
        {
            if (row.Kind == ReplyAlertKinds.Nest && row.ForumId is Guid forumId && forums.TryGetValue(forumId, out var forum))
            {
                return new ReplyAlertDto(
                    row.Id,
                    row.ReplyCount <= 1 ? row.RootPostId : null,
                    forum.Name,
                    row.ReplyCount,
                    row.LatestReplyId,
                    row.LatestReplyAt,
                    row.CreatedAt,
                    row.ReadAt,
                    row.Kind,
                    row.ForumId,
                    null,
                    forum.Slug,
                    forum.ParentSlug,
                    null);
            }

            if (row.Kind == ReplyAlertKinds.Watercooler && row.WatercoolerId is Guid coolerId && coolers.TryGetValue(coolerId, out var cooler))
            {
                return new ReplyAlertDto(
                    row.Id,
                    row.ReplyCount <= 1 ? row.RootPostId : null,
                    cooler.Name,
                    row.ReplyCount,
                    row.LatestReplyId,
                    row.LatestReplyAt,
                    row.CreatedAt,
                    row.ReadAt,
                    row.Kind,
                    null,
                    row.WatercoolerId,
                    cooler.Slug,
                    cooler.ParentSlug,
                    cooler.ForumParentSlug);
            }

            if (row.Kind == ReplyAlertKinds.Vote)
            {
                var voteTitle = row.RootPostId is Guid votedId && titles.TryGetValue(votedId, out var voted) ? voted : null;
                return new ReplyAlertDto(
                    row.Id,
                    row.RootPostId,
                    ThreadTitle(voteTitle),
                    row.ReplyCount,
                    row.LatestReplyId,
                    row.LatestReplyAt,
                    row.CreatedAt,
                    row.ReadAt,
                    ReplyAlertKinds.Vote,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            if (row.Kind == ReplyAlertKinds.Follow)
            {
                subjects.TryGetValue(row.SubjectUserId ?? Guid.Empty, out var subject);
                var name = subject is null
                    ? "Someone"
                    : !string.IsNullOrWhiteSpace(subject.DisplayName)
                        ? subject.DisplayName.Trim()
                        : !string.IsNullOrWhiteSpace(subject.Username)
                            ? subject.Username.Trim()
                            : "Someone";
                return new ReplyAlertDto(
                    row.Id,
                    row.ReplyCount <= 1 ? row.RootPostId : null,
                    name,
                    row.ReplyCount,
                    row.LatestReplyId,
                    row.LatestReplyAt,
                    row.CreatedAt,
                    row.ReadAt,
                    ReplyAlertKinds.Follow,
                    null,
                    null,
                    string.IsNullOrWhiteSpace(subject?.Username) ? null : subject.Username,
                    null,
                    null);
            }

            var title = row.RootPostId is Guid rootId && titles.TryGetValue(rootId, out var stored) ? stored : null;
            return new ReplyAlertDto(
                row.Id,
                row.RootPostId,
                ThreadTitle(title),
                row.ReplyCount,
                row.LatestReplyId,
                row.LatestReplyAt,
                row.CreatedAt,
                row.ReadAt,
                ReplyAlertKinds.Thread,
                null,
                null,
                null,
                null,
                null);
        }).ToList();
    }

    public Task<int> UnreadCountAsync(Guid userId, CancellationToken ct = default) =>
        _db.ReplyAlerts.CountAsync(a => a.RecipientUserId == userId && a.ReadAt == null, ct);

    public async Task MarkReadAsync(Guid userId, Guid alertId, CancellationToken ct = default)
    {
        var alert = await _db.ReplyAlerts.FirstOrDefaultAsync(
            a => a.Id == alertId && a.RecipientUserId == userId,
            ct);
        if (alert is null)
            throw new InvalidOperationException("Alert not found.");
        if (alert.ReadAt is null)
        {
            alert.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<ReplyAlertPreferenceDto> GetPreferenceAsync(Guid userId, Guid postId, CancellationToken ct = default)
    {
        var root = await RequireVisibleRootAsync(userId, postId, ct);
        var stored = await _db.ThreadAlertPreferences
            .Where(p => p.UserId == userId && p.RootPostId == root.Id)
            .Select(p => p.Mode)
            .FirstOrDefaultAsync(ct);
        return new ReplyAlertPreferenceDto(root.Id, stored ?? ReplyAlertModes.Replies);
    }

    public async Task<ReplyAlertPreferenceDto> SetPreferenceAsync(
        Guid userId,
        Guid postId,
        string mode,
        CancellationToken ct = default)
    {
        var normalized = ReplyAlertModes.Normalize(mode);
        var root = await RequireVisibleRootAsync(userId, postId, ct);
        var stored = await _db.ThreadAlertPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.RootPostId == root.Id, ct);
        if (stored is null)
        {
            stored = new ThreadAlertPreference
            {
                UserId = userId,
                RootPostId = root.Id,
                Mode = normalized,
                UpdatedAt = DateTime.UtcNow
            };
            _db.ThreadAlertPreferences.Add(stored);
        }
        else
        {
            stored.Mode = normalized;
            stored.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return new ReplyAlertPreferenceDto(root.Id, stored.Mode);
    }

    public async Task RegisterDeviceAsync(Guid userId, string platform, string token, CancellationToken ct = default)
    {
        var normalizedPlatform = platform.Trim().ToLowerInvariant();
        if (normalizedPlatform != "android")
            throw new InvalidOperationException("Push registration supports android.");

        var normalizedToken = token.Trim();
        if (normalizedToken.Length is < 8 or > 4096)
            throw new InvalidOperationException("Invalid push token.");

        var existing = await _db.PushDevices.FirstOrDefaultAsync(d => d.Token == normalizedToken, ct);
        var now = DateTime.UtcNow;
        if (existing is null)
        {
            _db.PushDevices.Add(new PushDevice
            {
                UserId = userId,
                Platform = normalizedPlatform,
                Token = normalizedToken,
                CreatedAt = now,
                LastSeenAt = now
            });
        }
        else
        {
            existing.UserId = userId;
            existing.Platform = normalizedPlatform;
            existing.LastSeenAt = now;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task UnregisterDeviceAsync(Guid userId, string token, CancellationToken ct = default)
    {
        var normalized = token.Trim();
        var existing = await _db.PushDevices
            .FirstOrDefaultAsync(d => d.UserId == userId && d.Token == normalized, ct);
        if (existing is null)
            return;
        _db.PushDevices.Remove(existing);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<PlaceAlertPreferenceDto> GetPlacePreferenceAsync(
        Guid userId,
        string kind,
        Guid placeId,
        CancellationToken ct = default)
    {
        var (normalizedKind, _) = await RequireVisiblePlaceAsync(userId, kind, placeId, ct);
        var stored = await FindPlacePreferenceAsync(userId, normalizedKind, placeId, ct);
        return new PlaceAlertPreferenceDto(normalizedKind, placeId, stored?.Mode ?? PlaceAlertModes.Off);
    }

    public async Task<PlaceAlertPreferenceDto> SetPlacePreferenceAsync(
        Guid userId,
        string kind,
        Guid placeId,
        string mode,
        CancellationToken ct = default)
    {
        var normalizedMode = PlaceAlertModes.Normalize(mode);
        var (normalizedKind, _) = await RequireVisiblePlaceAsync(userId, kind, placeId, ct);
        var stored = await FindPlacePreferenceAsync(userId, normalizedKind, placeId, ct);
        if (stored is null)
        {
            stored = new PlaceAlertPreference
            {
                UserId = userId,
                ForumId = normalizedKind == ReplyAlertKinds.Nest ? placeId : null,
                WatercoolerId = normalizedKind == ReplyAlertKinds.Watercooler ? placeId : null,
                Mode = normalizedMode,
                UpdatedAt = DateTime.UtcNow
            };
            _db.PlaceAlertPreferences.Add(stored);
        }
        else
        {
            stored.Mode = normalizedMode;
            stored.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return new PlaceAlertPreferenceDto(normalizedKind, placeId, stored.Mode);
    }

    private async Task RecordPlaceAsync(Post post, CancellationToken ct)
    {
        var kind = post.WatercoolerId.HasValue
            ? ReplyAlertKinds.Watercooler
            : post.ForumId.HasValue
                ? ReplyAlertKinds.Nest
                : null;
        if (kind is null)
            return;

        var placeId = kind == ReplyAlertKinds.Watercooler ? post.WatercoolerId!.Value : post.ForumId!.Value;
        var isNewPost = !post.ParentPostId.HasValue;
        var preferences = _db.PlaceAlertPreferences.AsQueryable();
        preferences = kind == ReplyAlertKinds.Watercooler
            ? preferences.Where(p => p.WatercoolerId == placeId)
            : preferences.Where(p => p.ForumId == placeId);
        preferences = isNewPost
            ? preferences.Where(p => p.Mode == PlaceAlertModes.Posts || p.Mode == PlaceAlertModes.All)
            : preferences.Where(p => p.Mode == PlaceAlertModes.All);

        var recipients = await preferences.Select(p => p.UserId).ToListAsync(ct);
        recipients.RemoveAll(id => id == post.AuthorUserId);
        if (recipients.Count == 0)
            return;

        var name = kind == ReplyAlertKinds.Watercooler
            ? await _db.Watercoolers.Where(w => w.Id == placeId).Select(w => w.Name).FirstOrDefaultAsync(ct)
            : await _db.Forums.Where(f => f.Id == placeId).Select(f => f.Name).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(name))
            name = kind == ReplyAlertKinds.Watercooler ? "a watercooler" : "a nest";

        string? slug;
        string? parentSlug;
        string? forumParentSlug;
        if (kind == ReplyAlertKinds.Watercooler)
        {
            var address = await _db.Watercoolers
                .Where(w => w.Id == placeId)
                .Select(w => new
                {
                    w.Slug,
                    ParentSlug = w.ParentForum != null ? w.ParentForum.Slug : null,
                    ForumParentSlug = w.ParentForum != null && w.ParentForum.ParentForum != null
                        ? w.ParentForum.ParentForum.Slug
                        : null
                })
                .FirstOrDefaultAsync(ct);
            slug = address?.Slug;
            parentSlug = address?.ParentSlug;
            forumParentSlug = address?.ForumParentSlug;
        }
        else
        {
            var address = await _db.Forums
                .Where(f => f.Id == placeId)
                .Select(f => new
                {
                    f.Slug,
                    ParentSlug = f.ParentForum != null ? f.ParentForum.Slug : null
                })
                .FirstOrDefaultAsync(ct);
            slug = address?.Slug;
            parentSlug = address?.ParentSlug;
            forumParentSlug = null;
        }

        var now = post.CreatedAt == default ? DateTime.UtcNow : post.CreatedAt;
        var root = isNewPost ? post : await ResolveRootAsync(post, ct);
        var authorName = await AuthorNameAsync(post.AuthorUserId, ct);
        var serverDid = await _db.NodeConfig.Select(n => n.Did).FirstOrDefaultAsync(ct) ?? "";
        var updated = new List<ReplyAlert>();

        foreach (var userId in recipients.Distinct())
        {
            var open = await _db.ReplyAlerts.FirstOrDefaultAsync(
                a => a.RecipientUserId == userId
                    && a.Kind == kind
                    && a.ReadAt == null
                    && (kind == ReplyAlertKinds.Watercooler ? a.WatercoolerId == placeId : a.ForumId == placeId),
                ct);
            if (open is null)
            {
                open = new ReplyAlert
                {
                    RecipientUserId = userId,
                    Kind = kind,
                    ForumId = kind == ReplyAlertKinds.Nest ? placeId : null,
                    WatercoolerId = kind == ReplyAlertKinds.Watercooler ? placeId : null,
                    RootPostId = root.Id,
                    ReplyCount = 1,
                    LatestReplyId = post.Id,
                    LatestReplyAt = now,
                    CreatedAt = now
                };
                _db.ReplyAlerts.Add(open);
            }
            else
            {
                open.ReplyCount += 1;
                open.LatestReplyId = post.Id;
                open.LatestReplyAt = now;
                open.RootPostId = root.Id;
            }

            updated.Add(open);
        }

        await _db.SaveChangesAsync(ct);

        foreach (var alert in updated)
        {
            var devices = await _db.PushDevices.Where(d => d.UserId == alert.RecipientUserId).ToListAsync(ct);
            if (devices.Count == 0)
                continue;

            var push = new ReplyAlertPush(
                alert.Id,
                alert.ReplyCount <= 1 ? alert.RootPostId : null,
                serverDid,
                name,
                PlaceAlertBody(alert.ReplyCount, authorName, name),
                alert.ReplyCount,
                kind,
                slug,
                parentSlug,
                forumParentSlug);
            try
            {
                var dropped = await _push.SendAsync(devices, push, ct);
                if (dropped.Count == 0)
                    continue;
                var stale = await _db.PushDevices.Where(d => dropped.Contains(d.Token)).ToListAsync(ct);
                _db.PushDevices.RemoveRange(stale);
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Place alert push failed for {AlertId}", alert.Id);
            }
        }
    }

    private async Task<(string Kind, Guid PlaceId)> RequireVisiblePlaceAsync(
        Guid userId,
        string kind,
        Guid placeId,
        CancellationToken ct)
    {
        if (string.Equals(kind, ReplyAlertKinds.Nest, StringComparison.OrdinalIgnoreCase))
        {
            var forum = await _db.Forums.FindAsync([placeId], ct)
                ?? throw new InvalidOperationException("Nest not found.");
            if (!await _permissions.CanViewForumAsync(userId, forum.Id, ct))
                throw new PermissionDeniedException(ForumPermissions.ViewForum.ToString(), "forum");
            return (ReplyAlertKinds.Nest, forum.Id);
        }

        if (string.Equals(kind, ReplyAlertKinds.Watercooler, StringComparison.OrdinalIgnoreCase))
        {
            var cooler = await _db.Watercoolers.FindAsync([placeId], ct)
                ?? throw new InvalidOperationException("Watercooler not found.");
            if (!await _permissions.CanViewWatercoolerAsync(userId, cooler.Id, ct))
                throw new PermissionDeniedException(WatercoolerPermissions.ViewWatercooler.ToString(), "watercooler");
            return (ReplyAlertKinds.Watercooler, cooler.Id);
        }

        throw new InvalidOperationException("Place alerts are for a nest or a watercooler.");
    }

    private Task<PlaceAlertPreference?> FindPlacePreferenceAsync(
        Guid userId,
        string kind,
        Guid placeId,
        CancellationToken ct) =>
        kind == ReplyAlertKinds.Watercooler
            ? _db.PlaceAlertPreferences.FirstOrDefaultAsync(p => p.UserId == userId && p.WatercoolerId == placeId, ct)
            : _db.PlaceAlertPreferences.FirstOrDefaultAsync(p => p.UserId == userId && p.ForumId == placeId, ct);

    private async Task<Post> RequireVisibleRootAsync(Guid userId, Guid postId, CancellationToken ct)
    {
        var post = await _db.Posts.FindAsync([postId], ct)
            ?? throw new InvalidOperationException("Thread not found.");
        var root = await ResolveRootAsync(post, ct);
        if (root.ForumId.HasValue)
        {
            if (!await _permissions.CanViewForumAsync(userId, root.ForumId.Value, ct))
                throw new PermissionDeniedException(ForumPermissions.ViewForum.ToString(), "forum");
        }
        else if (root.WatercoolerId.HasValue)
        {
            if (!await _permissions.CanViewWatercoolerAsync(userId, root.WatercoolerId.Value, ct))
                throw new PermissionDeniedException(WatercoolerPermissions.ViewWatercooler.ToString(), "watercooler");
        }
        else
        {
            throw new InvalidOperationException("Thread not found.");
        }

        return root;
    }

    private async Task<Post> ResolveRootAsync(Post start, CancellationToken ct)
    {
        var current = start;
        for (var depth = 0; depth < 64 && current.ParentPostId.HasValue; depth++)
        {
            var parent = await _db.Posts.FindAsync([current.ParentPostId.Value], ct);
            if (parent is null)
                break;
            current = parent;
        }

        return current;
    }

    private async Task RecordFollowedPostAsync(Post post, CancellationToken ct)
    {
        var author = await _db.Users.AsNoTracking()
            .Where(u => u.Id == post.AuthorUserId)
            .Select(u => new { u.Id, u.Did, u.AuthIssuerUrl, u.DisplayName, u.Username })
            .FirstOrDefaultAsync(ct);
        if (author is null || string.IsNullOrWhiteSpace(author.Did))
            return;

        var recipients = await WatcherUserIdsAsync(author.Did, author.AuthIssuerUrl, FollowAlertModes.WantsPosts, ct);
        recipients.RemoveAll(id => id == author.Id);
        if (recipients.Count == 0)
            return;

        var now = post.CreatedAt == default ? DateTime.UtcNow : post.CreatedAt;
        var serverDid = await ServerDidAsync(ct);
        var name = !string.IsNullOrWhiteSpace(author.DisplayName)
            ? author.DisplayName.Trim()
            : !string.IsNullOrWhiteSpace(author.Username)
                ? author.Username.Trim()
                : "Someone";
        var username = string.IsNullOrWhiteSpace(author.Username) ? null : author.Username.Trim();
        var updated = new List<ReplyAlert>();

        foreach (var userId in recipients.Distinct())
        {
            var open = await _db.ReplyAlerts.FirstOrDefaultAsync(
                a => a.RecipientUserId == userId
                    && a.Kind == ReplyAlertKinds.Follow
                    && a.SubjectUserId == author.Id
                    && a.ReadAt == null,
                ct);
            if (open is null)
            {
                open = new ReplyAlert
                {
                    RecipientUserId = userId,
                    Kind = ReplyAlertKinds.Follow,
                    SubjectUserId = author.Id,
                    RootPostId = post.Id,
                    ReplyCount = 1,
                    LatestReplyId = post.Id,
                    LatestReplyAt = now,
                    CreatedAt = now
                };
                _db.ReplyAlerts.Add(open);
            }
            else
            {
                open.ReplyCount += 1;
                open.RootPostId = post.Id;
                open.LatestReplyId = post.Id;
                open.LatestReplyAt = now;
            }

            updated.Add(open);
        }

        await _db.SaveChangesAsync(ct);
        await PushAsync(
            updated,
            alert => new ReplyAlertPush(
                alert.Id,
                alert.ReplyCount <= 1 ? post.Id : null,
                serverDid,
                name,
                alert.ReplyCount <= 1 ? $"New post from {name}" : $"{alert.ReplyCount} new posts from {name}",
                alert.ReplyCount,
                ReplyAlertKinds.Follow,
                username),
            ct);
    }

    private async Task<List<Guid>> WatcherUserIdsAsync(
        string? authorDid,
        string? issuerUrl,
        Func<string, bool> wants,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(authorDid))
            return [];

        var issuer = issuerUrl;
        if (string.IsNullOrWhiteSpace(issuer))
        {
            issuer = (await _db.EffectiveTrustedIssuersAsync(ct))
                .OrderBy(i => i.Name, StringComparer.Ordinal)
                .Select(i => i.IssuerUrl)
                .FirstOrDefault();
        }

        var watchers = await _follows.ListWatchersAsync(issuer, authorDid, ct);
        var dids = watchers
            .Where(watcher => wants(FollowAlertModes.NormalizeOrOff(watcher.AlertMode)))
            .Select(watcher => watcher.FollowerDid)
            .Where(did => !string.IsNullOrWhiteSpace(did))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (dids.Count == 0)
            return [];

        return await _db.Users.AsNoTracking()
            .Where(u => u.Did != null && dids.Contains(u.Did))
            .Select(u => u.Id)
            .ToListAsync(ct);
    }

    private async Task<int> VoteBaseAsync(CancellationToken ct)
    {
        var query = _db.NodeConfig.AsQueryable();
        if (_current?.ServerId is Guid serverId && serverId != Guid.Empty)
            query = query.Where(n => n.Id == serverId);
        var value = await query.OrderBy(n => n.CreatedAt).Select(n => (int?)n.VoteAlertBase).FirstOrDefaultAsync(ct);
        return VoteAlertLevels.NormalizeBase(value ?? VoteAlertLevels.DefaultBase);
    }

    private async Task<string> ServerDidAsync(CancellationToken ct) =>
        await _db.NodeConfig.Select(n => n.Did).FirstOrDefaultAsync(ct) ?? "";

    private async Task PushAsync(
        IReadOnlyList<ReplyAlert> alerts,
        Func<ReplyAlert, ReplyAlertPush> build,
        CancellationToken ct)
    {
        foreach (var alert in alerts)
        {
            var devices = await _db.PushDevices
                .Where(d => d.UserId == alert.RecipientUserId)
                .ToListAsync(ct);
            if (devices.Count == 0)
                continue;

            try
            {
                var dropped = await _push.SendAsync(devices, build(alert), ct);
                if (dropped.Count == 0)
                    continue;
                var stale = await _db.PushDevices.Where(d => dropped.Contains(d.Token)).ToListAsync(ct);
                _db.PushDevices.RemoveRange(stale);
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reply alert push failed for {AlertId}", alert.Id);
            }
        }
    }

    private async Task<string> AuthorNameAsync(Guid userId, CancellationToken ct)
    {
        var author = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.DisplayName, u.Username })
            .FirstOrDefaultAsync(ct);
        if (author is null)
            return "Someone";
        if (!string.IsNullOrWhiteSpace(author.DisplayName))
            return author.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(author.Username))
            return author.Username.Trim();
        return "Someone";
    }

    internal static string ThreadTitle(Post root) => ThreadTitle(root.Title);

    internal static string ThreadTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) ? "a thread" : title.Trim();

    internal static string AlertBody(int count, string authorName, string title) =>
        count <= 1
            ? $"{authorName} replied to {title}"
            : $"{count} new replies on {title}";

    internal static string PlaceAlertBody(int count, string authorName, string placeName) =>
        count <= 1
            ? $"{authorName} posted in {placeName}"
            : $"{count} new posts in {placeName}";

    internal static string VoteAlertBody(int level, string title) => $"{level} votes on {title}";
}
