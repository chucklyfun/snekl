namespace Snekl.Domain.Entities;

public class HubOutboxMessage : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public string HubBaseUrl { get; set; } = "";
    public string Path { get; set; } = "";
    public string EnvelopeType { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAt { get; set; }
    public string? LastError { get; set; }
}
