using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;

namespace Snekl.Infrastructure.Services;

public static class StatusPresets
{
    public static IReadOnlyList<StatusPresetInfo> All { get; } =
    [
        new("live", "Live streaming", "Live now", "🔴", "PulseRing", "#ef4444", 8),
        new("at_event", "At an event", "At an event", "📍", "Ring", "#4f8cff", 24),
        new("supporting_cause", "Supporting a cause", "Supporting a cause", "💙", "Corner", "#7c5cff", 168),
        new("safe", "Marked safe", "Marked safe", "🛡️", "Ring", "#22c55e", 72),
        new("preparing", "Preparing for event", "Getting ready", "⏳", "Corner", "#f59e0b", 4),
        new("custom", "Custom status", "", null, "Corner", null, 24)
    ];

    public static StatusPresetInfo? Get(string preset) =>
        All.FirstOrDefault(p => p.Preset.Equals(preset, StringComparison.OrdinalIgnoreCase));

    public static StatusPreset Parse(string preset) =>
        Enum.TryParse<StatusPreset>(preset, true, out var value) ? value : StatusPreset.Custom;
}

public static class PresentationDefaults
{
    public static string SeverityColor(AnnotationSeverity severity) => severity switch
    {
        AnnotationSeverity.Info => "#8b9cb3",
        AnnotationSeverity.Notice => "#4f8cff",
        AnnotationSeverity.Highlight => "#7c5cff",
        AnnotationSeverity.Warn => "#f59e0b",
        AnnotationSeverity.Restrict => "#ef4444",
        _ => "#8b9cb3"
    };
}

public static class PresentationMapper
{
    public static OverlayDto? ToOverlayDto(OverlayKind? kind, string? emoji, string? iconUrl, string? color, string? ariaLabel) =>
        kind is null ? null : new OverlayDto(kind.Value.ToString(), emoji, iconUrl, color, "bottom-right", ariaLabel);

    public static OverlayDto? StatusOverlay(UserStatus status) =>
        ToOverlayDto(status.OverlayKind, status.OverlayEmoji, status.OverlayIconUrl, status.OverlayColor, status.OverlayAriaLabel);

    public static OverlayDto? EntryOverlay(ListEntry entry) =>
        ToOverlayDto(entry.OverlayKind, entry.OverlayEmoji, entry.OverlayIconUrl, entry.OverlayColor, entry.OverlayAriaLabel);

    public static UserFlairDto? ToFlairDto(User user)
    {
        if (user.FlairEmoji is null && user.FlairIconUrl is null && user.FlairLabel is null &&
            user.NameColor is null && user.NameStyle is null)
            return null;

        return new UserFlairDto(user.FlairEmoji, user.FlairIconUrl, user.FlairLabel, user.NameColor, user.NameStyle);
    }

    public static UserStatusDto ToStatusDto(UserStatus status) => new(
        status.Preset.ToString(),
        status.Message,
        status.Emoji,
        status.IconUrl,
        status.LinkUrl,
        status.EventName,
        status.ExpiresAt,
        StatusOverlay(status),
        status.DisplayNameSuffix,
        status.NameColorOverride);

    public static ListAnnotationDto ToAnnotationDto(ListEntry entry, CuratedList list) => new(
        list.Id,
        list.Name,
        (int)entry.Severity,
        entry.TooltipText,
        entry.FlairEmoji ?? list.DefaultFlairEmoji,
        entry.FlairIconUrl ?? list.DefaultIconUrl,
        entry.UsernameColor,
        entry.UsernameStyle,
        EntryOverlay(entry),
        entry.AddedAt);
}
