namespace Snekl.Domain.Entities;

public class ForumMemberBan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ForumId { get; set; }
    public Forum? Forum { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid BannedByUserId { get; set; }
    public User? BannedBy { get; set; }
    public string? Reason { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
