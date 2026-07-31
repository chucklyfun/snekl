namespace Snekl.Domain.Entities;

public class PeerNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BaseUrl { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid RemoteNodeId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncedAt { get; set; }
}
