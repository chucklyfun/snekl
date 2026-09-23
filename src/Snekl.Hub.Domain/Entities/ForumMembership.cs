namespace Snekl.Hub.Domain.Entities;

public class ForumMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserDid { get; set; } = string.Empty;
    public string ServerDid { get; set; } = string.Empty;
    public Guid ForumId { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
