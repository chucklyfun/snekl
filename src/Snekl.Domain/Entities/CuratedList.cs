using Snekl.Domain.Enums;

namespace Snekl.Domain.Entities;

public class CuratedList
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid OwnerUserId { get; set; }
    public User? Owner { get; set; }
    public ListBehavior DefaultBehavior { get; set; } = ListBehavior.Highlight;
    public string? DefaultFlairEmoji { get; set; }
    public string? DefaultIconUrl { get; set; }
    public bool IsPublic { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ListEntry> Entries { get; set; } = new List<ListEntry>();
    public ICollection<ListSubscription> Subscriptions { get; set; } = new List<ListSubscription>();
}
