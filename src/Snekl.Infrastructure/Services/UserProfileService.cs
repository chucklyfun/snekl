using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IUserProfileService
{
    Task UpdateFlairAsync(Guid userId, UpdateUserFlairRequest request, CancellationToken ct = default);
    Task<UserStatusDto> SetStatusAsync(Guid userId, SetUserStatusRequest request, CancellationToken ct = default);
    Task ClearStatusAsync(Guid userId, CancellationToken ct = default);
    IReadOnlyList<StatusPresetInfo> GetStatusPresets();
}

public class UserProfileService : IUserProfileService
{
    private readonly SneklDbContext _db;
    private readonly IUserPresentationService _presentation;

    public UserProfileService(SneklDbContext db, IUserPresentationService presentation)
    {
        _db = db;
        _presentation = presentation;
    }

    public async Task UpdateFlairAsync(Guid userId, UpdateUserFlairRequest request, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct)
            ?? throw new InvalidOperationException("User not found.");

        user.FlairEmoji = Normalize(request.Emoji);
        user.FlairIconUrl = Normalize(request.IconUrl);
        user.FlairLabel = Normalize(request.Label);
        user.NameColor = Normalize(request.NameColor);
        user.NameStyle = Normalize(request.NameStyle);
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<UserStatusDto> SetStatusAsync(Guid userId, SetUserStatusRequest request, CancellationToken ct = default)
    {
        if (request.ExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException("Status expiry must be in the future.");

        var user = await _db.Users.Include(u => u.ActiveStatus).FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("User not found.");

        var preset = StatusPresets.Parse(request.Preset);
        var presetDefaults = StatusPresets.Get(request.Preset);

        if (user.ActiveStatus is not null)
            _db.UserStatuses.Remove(user.ActiveStatus);

        var overlay = request.AvatarOverlay;
        var status = new UserStatus
        {
            UserId = userId,
            Preset = preset,
            Message = Normalize(request.Message) ?? presetDefaults?.DefaultMessage,
            Emoji = Normalize(request.Emoji) ?? presetDefaults?.DefaultEmoji,
            IconUrl = Normalize(request.IconUrl),
            LinkUrl = Normalize(request.LinkUrl),
            EventName = Normalize(request.EventName),
            ExpiresAt = request.ExpiresAt,
            DisplayNameSuffix = Normalize(request.DisplayNameSuffix),
            NameColorOverride = Normalize(request.NameColorOverride),
            OverlayKind = overlay is not null && Enum.TryParse<OverlayKind>(overlay.Kind, true, out var kind)
                ? kind
                : presetDefaults is not null && Enum.TryParse<OverlayKind>(presetDefaults.DefaultOverlayKind, true, out var defaultKind)
                    ? defaultKind
                    : null,
            OverlayColor = overlay?.Color ?? presetDefaults?.DefaultOverlayColor,
            OverlayEmoji = overlay?.Emoji ?? presetDefaults?.DefaultEmoji,
            OverlayIconUrl = overlay?.IconUrl,
            OverlayAriaLabel = overlay?.AriaLabel ?? presetDefaults?.Label
        };

        _db.UserStatuses.Add(status);
        await _db.SaveChangesAsync(ct);

        return PresentationMapper.ToStatusDto(status);
    }

    public async Task ClearStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var status = await _db.UserStatuses.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (status is null) return;
        _db.UserStatuses.Remove(status);
        await _db.SaveChangesAsync(ct);
    }

    public IReadOnlyList<StatusPresetInfo> GetStatusPresets() => StatusPresets.All;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
