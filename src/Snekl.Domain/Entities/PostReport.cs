namespace Snekl.Domain.Entities;

public class PostReport : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public Post? Post { get; set; }
    public Guid ForumId { get; set; }
    public Forum? Forum { get; set; }
    public Guid ReporterUserId { get; set; }
    public User? Reporter { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = PostReportStatus.Open;
    public Guid? ResolvedByUserId { get; set; }
    public User? ResolvedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}

public static class PostReportStatus
{
    public const string Open = "open";
    public const string Dismissed = "dismissed";
    public const string Resolved = "resolved";
}
