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
    Task<UserPresentationDto> GetPresentationAsync(Guid userId, Guid? viewerUserId = null, CancellationToken ct = default);
    Task<UserPresentationDto?> GetPresentationByUsernameAsync(string username, Guid? viewerUserId = null, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, UserPresentationDto>> GetPresentationsAsync(IEnumerable<Guid> userIds, Guid? viewerUserId = null, CancellationToken ct = default);
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

    public async Task<UserPresentationDto> GetPresentationAsync(Guid userId, Guid? viewerUserId = null, CancellationToken ct = default)
    {
        var cacheKey = CacheKeys.Presentation(userId, viewerUserId);
        var cached = await _cache.GetJsonAsync<UserPresentationDto>(cacheKey, ct);
        if (cached is not null) return cached;

        await ClearExpiredStatusesAsync(ct);
        var map = await LoadPresentationsAsync([userId], viewerUserId, ct);
        var presentation = map[userId];
        await _cache.SetJsonAsync(cacheKey, presentation, PresentationTtl, ct);
        return presentation;
    }

    public async Task<UserPresentationDto?> GetPresentationByUsernameAsync(string username, Guid? viewerUserId = null, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username.ToLowerInvariant(), ct);
        return user is null ? null : await GetPresentationAsync(user.Id, viewerUserId, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, UserPresentationDto>> GetPresentationsAsync(
        IEnumerable<Guid> userIds,
        Guid? viewerUserId = null,
        CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        var result = new Dictionary<Guid, UserPresentationDto>();
        var missing = new List<Guid>();

        foreach (var id in ids)
        {
            var cached = await _cache.GetJsonAsync<UserPresentationDto>(CacheKeys.Presentation(id, viewerUserId), ct);
            if (cached is not null) result[id] = cached;
            else missing.Add(id);
        }

        if (missing.Count > 0)
        {
            await ClearExpiredStatusesAsync(ct);
            var loaded = await LoadPresentationsAsync(missing, viewerUserId, ct);
            foreach (var (id, presentation) in loaded)
            {
                result[id] = presentation;
                await _cache.SetJsonAsync(CacheKeys.Presentation(id, viewerUserId), presentation, PresentationTtl, ct);
            }
        }

        return result;
    }

    public async Task InvalidatePresentationAsync(Guid userId, CancellationToken ct = default)
    {
        await _cache.RemoveAsync(CacheKeys.Presentation(userId, null), ct);
        await _cache.RemoveAsync(CacheKeys.Presentation(userId, userId), ct);
    }

    private async Task<IReadOnlyDictionary<Guid, UserPresentationDto>> LoadPresentationsAsync(
        IReadOnlyList<Guid> ids,
        Guid? viewerUserId,
        CancellationToken ct)
    {
        var users = await _db.Users
            .Include(u => u.ActiveStatus)
            .Where(u => ids.Contains(u.Id))
            .ToListAsync(ct);

        var subscribedListIds = viewerUserId.HasValue
            ? await _db.ListSubscriptions
                .Where(s => s.UserId == viewerUserId.Value)
                .Select(s => s.ListId)
                .ToListAsync(ct)
            : [];

        var entries = subscribedListIds.Count > 0
            ? await _db.ListEntries
                .Include(e => e.List)
                .Where(e => ids.Contains(e.TargetUserId) && subscribedListIds.Contains(e.ListId))
                .ToListAsync(ct)
            : [];

        var result = new Dictionary<Guid, UserPresentationDto>();
        foreach (var user in users)
        {
            var userEntries = entries.Where(e => e.TargetUserId == user.Id).OrderByDescending(e => e.Severity).ToList();
            result[user.Id] = BuildPresentation(user, userEntries);
        }

        foreach (var id in ids.Where(id => !result.ContainsKey(id)))
        {
            result[id] = new UserPresentationDto(
                id, "unknown", "Unknown", "", null, false, null, null, "Unknown", null, null, [], null);
        }

        return result;
    }

    internal static UserPresentationDto BuildPresentation(User user, IReadOnlyList<ListEntry> entries)
    {
        var flair = PresentationMapper.ToFlairDto(user);
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
            ?? flair?.NameColor;

        var effectiveStyle = topEntry?.UsernameStyle ?? flair?.NameStyle;

        var suffix = status?.DisplayNameSuffix ?? string.Empty;
        var effectiveDisplayName = user.DisplayName + suffix;

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
            avatarOverlay);
    }

    private static OverlayDto? ResolveAvatarOverlay(UserStatusDto? status, IReadOnlyList<ListAnnotationDto> annotations)
    {
        if (status?.AvatarOverlay is not null) return status.AvatarOverlay;
        return annotations.Select(a => a.AvatarOverlay).FirstOrDefault(o => o is not null);
    }
}
