using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Caching;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IUserPresentationService
{
    Task ClearExpiredStatusesAsync(CancellationToken ct = default);
    Task<UserPresentationDto> GetPresentationAsync(Guid userId, Guid? viewerUserId = null, Guid? forumId = null, CancellationToken ct = default);
    Task<UserPresentationDto?> GetPresentationByUsernameAsync(string username, Guid? viewerUserId = null, Guid? forumId = null, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, UserPresentationDto>> GetPresentationsAsync(IEnumerable<Guid> userIds, Guid? viewerUserId = null, Guid? forumId = null, CancellationToken ct = default);
    Task InvalidatePresentationAsync(Guid userId, CancellationToken ct = default);
}

public class UserPresentationService : IUserPresentationService
{
    private static readonly TimeSpan PresentationTtl = TimeSpan.FromSeconds(60);
    private readonly SneklDbContext _db;
    private readonly IDistributedCache _cache;

    public UserPresentationService(SneklDbContext db, IDistributedCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task ClearExpiredStatusesAsync(CancellationToken ct = default)
    {
        var expired = await _db.UserStatuses.Where(s => s.ExpiresAt <= DateTime.UtcNow).ToListAsync(ct);
        if (expired.Count == 0) return;
        _db.UserStatuses.RemoveRange(expired);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<UserPresentationDto> GetPresentationAsync(Guid userId, Guid? viewerUserId = null, Guid? forumId = null, CancellationToken ct = default)
    {
        var cacheKey = CacheKeys.Presentation(userId, viewerUserId, forumId);
        var cached = await _cache.GetJsonAsync<UserPresentationDto>(cacheKey, ct);
        if (cached is not null) return cached;

        await ClearExpiredStatusesAsync(ct);
        var map = await LoadPresentationsAsync([userId], viewerUserId, forumId, ct);
        var presentation = map[userId];
        await _cache.SetJsonAsync(cacheKey, presentation, PresentationTtl, ct);
        return presentation;
    }

    public async Task<UserPresentationDto?> GetPresentationByUsernameAsync(string username, Guid? viewerUserId = null, Guid? forumId = null, CancellationToken ct = default)
    {
        var handle = username.Trim().TrimStart('@').ToLowerInvariant();
        if (handle.Length == 0) return null;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == handle, ct);
        return user is null ? null : await GetPresentationAsync(user.Id, viewerUserId, forumId, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, UserPresentationDto>> GetPresentationsAsync(
        IEnumerable<Guid> userIds,
        Guid? viewerUserId = null,
        Guid? forumId = null,
        CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        var result = new Dictionary<Guid, UserPresentationDto>();
        var missing = new List<Guid>();

        foreach (var id in ids)
        {
            var cached = await _cache.GetJsonAsync<UserPresentationDto>(CacheKeys.Presentation(id, viewerUserId, forumId), ct);
            if (cached is not null) result[id] = cached;
            else missing.Add(id);
        }

        if (missing.Count > 0)
        {
            await ClearExpiredStatusesAsync(ct);
            var loaded = await LoadPresentationsAsync(missing, viewerUserId, forumId, ct);
            foreach (var (id, presentation) in loaded)
            {
                result[id] = presentation;
                await _cache.SetJsonAsync(CacheKeys.Presentation(id, viewerUserId, forumId), presentation, PresentationTtl, ct);
            }
        }

        return result;
    }

    public async Task InvalidatePresentationAsync(Guid userId, CancellationToken ct = default)
    {
        await _cache.RemoveAsync(CacheKeys.Presentation(userId, null, null), ct);
        await _cache.RemoveAsync(CacheKeys.Presentation(userId, userId, null), ct);
    }

    private async Task<IReadOnlyDictionary<Guid, UserPresentationDto>> LoadPresentationsAsync(
        IReadOnlyList<Guid> ids,
        Guid? viewerUserId,
        Guid? forumId,
        CancellationToken ct)
    {
        var users = await _db.Users
            .Include(u => u.ActiveStatus)
            .Where(u => ids.Contains(u.Id))
            .ToListAsync(ct);

        var subscribed = viewerUserId.HasValue
            ? await _db.ListSubscriptions
                .Where(s => s.UserId == viewerUserId.Value)
                .OrderBy(s => s.Priority)
                .ThenBy(s => s.SubscribedAt)
                .ToListAsync(ct)
            : [];

        var subscribedListIds = subscribed.Select(s => s.ListId).ToList();
        var priorityByList = subscribed.ToDictionary(s => s.ListId, s => s.Priority);

        var ignored = subscribedListIds.Count > 0
            ? (await _db.ListEntryIgnores
                .Where(i => i.UserId == viewerUserId!.Value && subscribedListIds.Contains(i.ListId))
                .Select(i => new { i.ListId, i.TargetUserId })
                .ToListAsync(ct))
                .Select(i => (i.ListId, i.TargetUserId))
                .ToHashSet()
            : [];

        var entries = subscribedListIds.Count > 0
            ? await _db.ListEntries
                .Include(e => e.List)
                .Where(e => ids.Contains(e.TargetUserId) && subscribedListIds.Contains(e.ListId))
                .ToListAsync(ct)
            : [];
        entries = entries.Where(e => !ignored.Contains((e.ListId, e.TargetUserId))).ToList();

        var forumContext = forumId.HasValue
            ? await LoadForumContextAsync(forumId.Value, ids, ct)
            : null;

        var result = new Dictionary<Guid, UserPresentationDto>();
        foreach (var user in users)
        {
            var userEntries = entries
                .Where(e => e.TargetUserId == user.Id)
                .OrderBy(e => priorityByList.GetValueOrDefault(e.ListId, int.MaxValue))
                .ThenByDescending(e => e.Severity)
                .ToList();
            ForumMemberContext? memberCtx = null;
            forumContext?.TryGetValue(user.Id, out memberCtx);
            result[user.Id] = BuildPresentation(user, userEntries, memberCtx);
        }

        foreach (var id in ids.Where(id => !result.ContainsKey(id)))
        {
            result[id] = new UserPresentationDto(
                id, "unknown", "Unknown", "", null, false, null, null, "Unknown", null, null, [], null);
        }

        return result;
    }

    private async Task<Dictionary<Guid, ForumMemberContext>> LoadForumContextAsync(
        Guid forumId,
        IReadOnlyList<Guid> userIds,
        CancellationToken ct)
    {
        var chain = await GetForumChainIdsAsync(forumId, ct);
        var result = new Dictionary<Guid, ForumMemberContext>();

        foreach (var userId in userIds)
        {
            ForumMemberContext? ctx = null;
            foreach (var fId in chain.AsEnumerable().Reverse())
            {
                var alias = await _db.ForumMemberAliases
                    .FirstOrDefaultAsync(a => a.ForumId == fId && a.UserId == userId, ct);
                if (alias is not null)
                {
                    ctx ??= new ForumMemberContext();
                    ctx.Alias = alias.Alias;
                }

                var flair = await _db.ForumMemberFlairs
                    .Include(f => f.Template)
                    .FirstOrDefaultAsync(f => f.ForumId == fId && f.UserId == userId, ct);
                if (flair?.Template is not null)
                {
                    ctx ??= new ForumMemberContext();
                    ctx.Flair = flair.Template;
                }

                var roleColor = await _db.ForumRoleAssignments
                    .Include(a => a.Role)
                    .Where(a => a.ForumId == fId && a.UserId == userId && a.Role!.Color != null)
                    .OrderByDescending(a => a.Role!.Position)
                    .Select(a => a.Role!.Color)
                    .FirstOrDefaultAsync(ct);
                if (roleColor is not null)
                {
                    ctx ??= new ForumMemberContext();
                    ctx.RoleColor ??= roleColor;
                }
            }

            if (ctx is not null)
                result[userId] = ctx;
        }

        return result;
    }

    private async Task<List<Guid>> GetForumChainIdsAsync(Guid forumId, CancellationToken ct)
    {
        var chain = new List<Guid>();
        var currentId = (Guid?)forumId;
        while (currentId.HasValue)
        {
            var forum = await _db.Forums.AsNoTracking().FirstOrDefaultAsync(f => f.Id == currentId.Value, ct);
            if (forum is null) break;
            chain.Insert(0, forum.Id);
            currentId = forum.ParentForumId;
        }
        return chain;
    }

    private static UserPresentationDto BuildPresentation(
        User user,
        IReadOnlyList<ListEntry> entries,
        ForumMemberContext? forumCtx = null)
    {
        var flair = forumCtx?.Flair is not null
            ? new UserFlairDto(forumCtx.Flair.Emoji, forumCtx.Flair.IconUrl, forumCtx.Flair.Label, forumCtx.Flair.NameColor, forumCtx.Flair.NameStyle)
            : PresentationMapper.ToFlairDto(user);

        var status = user.ActiveStatus is not null && user.ActiveStatus.ExpiresAt > DateTime.UtcNow
            ? PresentationMapper.ToStatusDto(user.ActiveStatus)
            : null;

        var annotations = entries
            .Where(e => e.List is not null)
            .Select(e => PresentationMapper.ToAnnotationDto(e, e.List!))
            .ToList();

        var topEntry = entries.FirstOrDefault();
        var effectiveColor = status?.NameColorOverride
            ?? topEntry?.UsernameColor
            ?? (topEntry is not null ? PresentationDefaults.SeverityColor(topEntry.Severity) : null)
            ?? flair?.NameColor
            ?? forumCtx?.RoleColor;

        var effectiveStyle = topEntry?.UsernameStyle ?? flair?.NameStyle;

        var suffix = status?.DisplayNameSuffix ?? string.Empty;
        var effectiveDisplayName = (forumCtx?.Alias ?? user.DisplayName) + suffix;

        var avatarOverlay = ResolveAvatarOverlay(status, annotations);

        return new UserPresentationDto(
            user.Id,
            user.Username,
            user.DisplayName,
            user.Bio,
            user.OriginNodeUrl,
            user.IsFederated,
            effectiveColor,
            effectiveStyle,
            effectiveDisplayName,
            flair,
            status,
            annotations,
            avatarOverlay,
            user.IsBot,
            user.Did);
    }

    private static OverlayDto? ResolveAvatarOverlay(UserStatusDto? status, IReadOnlyList<ListAnnotationDto> annotations)
    {
        if (status?.AvatarOverlay is not null) return status.AvatarOverlay;
        return annotations.Select(a => a.AvatarOverlay).FirstOrDefault(o => o is not null);
    }

    private sealed class ForumMemberContext
    {
        public string? Alias { get; set; }
        public ForumFlairTemplate? Flair { get; set; }
        public string? RoleColor { get; set; }
    }
}
