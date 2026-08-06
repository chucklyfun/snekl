using Snekl.Domain.Enums;

namespace Snekl.Domain.Entities;

public class ListEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListId { get; set; }
    public CuratedList? List { get; set; }
    public ListTargetType TargetType { get; set; } = ListTargetType.User;
    public Guid TargetUserId { get; set; }
    public User? TargetUser { get; set; }
    public AnnotationSeverity Severity { get; set; } = AnnotationSeverity.Notice;
    public string Label { get; set; } = string.Empty;
    public string TooltipText { get; set; } = string.Empty;
    public string? FlairEmoji { get; set; }
    public string? FlairIconUrl { get; set; }
    public string? UsernameColor { get; set; }
    public string? UsernameStyle { get; set; }
    public OverlayKind? OverlayKind { get; set; }
    public string? OverlayColor { get; set; }
    public string? OverlayEmoji { get; set; }
    public string? OverlayIconUrl { get; set; }
    public string? OverlayAriaLabel { get; set; }
    public Guid AddedByUserId { get; set; }
    public User? AddedBy { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
