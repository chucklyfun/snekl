namespace Snekl.Hub.Domain.Entities;

public class RegisteredServer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ServerDid { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastHeartbeatAt { get; set; }
    public long AckedMapVersion { get; set; }
    public string Status { get; set; } = "pending";
    public string InitiatedBy { get; set; } = "origin";
    public Guid? OriginInviteId { get; set; }
}
