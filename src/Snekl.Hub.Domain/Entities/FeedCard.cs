namespace Snekl.Hub.Domain.Entities;

public class FeedCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ServerDid { get; set; } = string.Empty;
    public Guid PostId { get; set; }
    public Guid ForumId { get; set; }
    public string ForumSlug { get; set; } = string.Empty;
    public string ForumName { get; set; } = string.Empty;
    public string OriginBaseUrl { get; set; } = string.Empty;
    public string OriginPostUrl { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Excerpt { get; set; } = string.Empty;
    public string? ImagePreviewUrl { get; set; }
    public string AuthorDid { get; set; } = string.Empty;
    public string AuthorDisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public string Visibility { get; set; } = "public";
    public int Score { get; set; }
    public int CommentCount { get; set; }
    public long MapVersion { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public DateTime EventAt { get; set; }
    public bool IsDeleted { get; set; }
    public string? NsfwCover { get; set; }
}
