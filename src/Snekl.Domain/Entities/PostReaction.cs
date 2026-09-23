namespace Snekl.Domain.Entities;

public class PostReaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public Post? Post { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid? EmoteId { get; set; }
    public Emote? Emote { get; set; }
    public string? Emoji { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
