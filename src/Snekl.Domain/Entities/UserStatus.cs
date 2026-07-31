using Snekl.Domain.Enums;

namespace Snekl.Domain.Entities;

public class UserStatus
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public StatusPreset Preset { get; set; } = StatusPreset.Custom;
    public string? Message { get; set; }
    public string? Emoji { get; set; }
    public string? IconUrl { get; set; }
    public string? LinkUrl { get; set; }
    public string? EventName { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public OverlayKind? OverlayKind { get; set; }
    public string? OverlayColor { get; set; }
    public string? OverlayEmoji { get; set; }
    public string? OverlayIconUrl { get; set; }
    public string? OverlayAriaLabel { get; set; }
    public string? DisplayNameSuffix { get; set; }
    public string? NameColorOverride { get; set; }
}
