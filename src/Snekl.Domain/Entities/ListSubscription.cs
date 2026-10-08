using Snekl.Domain.Enums;

namespace Snekl.Domain.Entities;

public class ListSubscription : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid ListId { get; set; }
    public CuratedList? List { get; set; }
    public ListBehavior AppliedBehavior { get; set; } = ListBehavior.Highlight;
    public int Priority { get; set; }
    public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;
}
