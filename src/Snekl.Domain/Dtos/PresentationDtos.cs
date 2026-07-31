namespace Snekl.Domain.Dtos;

public record OverlayDto(
    string Kind,
    string? Emoji,
    string? IconUrl,
    string? Color,
    string? Position,
    string? AriaLabel);

public record UserFlairDto(
    string? Emoji,
    string? IconUrl,
    string? Label,
    string? NameColor,
    string? NameStyle);

public record UserStatusDto(
    string Preset,
    string? Message,
    string? Emoji,
    string? IconUrl,
    string? LinkUrl,
    string? EventName,
    DateTime ExpiresAt,
    OverlayDto? AvatarOverlay,
    string? DisplayNameSuffix,
    string? NameColorOverride);

public record ListAnnotationDto(
    Guid ListId,
    string ListName,
    int Severity,
    string TooltipText,
    string? FlairEmoji,
    string? FlairIconUrl,
    string? UsernameColor,
    string? UsernameStyle,
    OverlayDto? AvatarOverlay,
    DateTime AddedAt);

public record UserPresentationDto(
    Guid Id,
    string Username,
    string DisplayName,
    string Bio,
    string? OriginNodeUrl,
    bool IsFederated,
    string? EffectiveNameColor,
    string? EffectiveNameStyle,
    string EffectiveDisplayName,
    UserFlairDto? Flair,
    UserStatusDto? ActiveStatus,
    IReadOnlyList<ListAnnotationDto> Annotations,
    OverlayDto? AvatarOverlay);

public record UpdateUserFlairRequest(
    string? Emoji,
    string? IconUrl,
    string? Label,
    string? NameColor,
    string? NameStyle);

public record SetUserStatusRequest(
    string Preset,
    string? Message,
    string? Emoji,
    string? IconUrl,
    string? LinkUrl,
    string? EventName,
    DateTime ExpiresAt,
    OverlayDto? AvatarOverlay,
    string? DisplayNameSuffix,
    string? NameColorOverride);

public record CreateCuratedListRequest(
    string Name,
    string Description,
    string DefaultBehavior,
    string? DefaultFlairEmoji,
    string? DefaultIconUrl,
    bool IsPublic);

public record CuratedListDto(
    Guid Id,
    string Name,
    string Description,
    string DefaultBehavior,
    string? DefaultFlairEmoji,
    string? DefaultIconUrl,
    bool IsPublic,
    int EntryCount,
    int SubscriberCount,
    DateTime CreatedAt,
    bool IsOwner);

public record AddListEntryRequest(
    Guid TargetUserId,
    int Severity,
    string TooltipText,
    string? FlairEmoji,
    string? FlairIconUrl,
    string? UsernameColor,
    string? UsernameStyle,
    OverlayDto? AvatarOverlay);

public record SubscribeListRequest(string AppliedBehavior);

public record StatusPresetInfo(
    string Preset,
    string Label,
    string DefaultMessage,
    string? DefaultEmoji,
    string DefaultOverlayKind,
    string? DefaultOverlayColor,
    int DefaultDurationHours);
