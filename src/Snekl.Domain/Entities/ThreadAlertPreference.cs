namespace Snekl.Domain.Entities;

public class ThreadAlertPreference : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid RootPostId { get; set; }
    public Post? RootPost { get; set; }
    public string Mode { get; set; } = Enums.ReplyAlertModes.Replies;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
