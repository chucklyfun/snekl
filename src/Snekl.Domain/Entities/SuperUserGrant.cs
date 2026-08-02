namespace Snekl.Domain.Entities;

public class SuperUserGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid GrantedBySiteAdminId { get; set; }
    public SiteAdmin? GrantedBySiteAdmin { get; set; }
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }
}
