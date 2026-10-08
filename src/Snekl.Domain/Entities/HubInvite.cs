namespace Snekl.Domain.Entities;

public class HubInvite : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public string HubBaseUrl { get; set; } = string.Empty;
    public string HubName { get; set; } = string.Empty;
    public string RequestedByDid { get; set; } = string.Empty;
    public string RequestedByUsername { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
