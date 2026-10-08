namespace Snekl.Domain.Entities;

/// <summary>Previous public address for a nest. The target id stays stable when the nest moves again.</summary>
public class ForumRedirect : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Top-level ancestor slug. Empty when the old address was a top-level nest.</summary>
    public string TopSlug { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;
    public Guid TargetForumId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
