namespace Snekl.Hub.Domain.Entities;

public class HubOperator
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Did { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public Guid? InvitedByOperatorId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
