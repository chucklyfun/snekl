namespace Snekl.Domain.Entities;

public class SiteAdmin
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string? Did { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public Guid? InvitedByAdminId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
