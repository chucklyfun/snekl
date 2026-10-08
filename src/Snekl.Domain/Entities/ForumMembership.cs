namespace Snekl.Domain.Entities;

public class ForumMembership : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ForumId { get; set; }
    public Forum? Forum { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
