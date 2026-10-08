namespace Snekl.Domain.Entities;

public class ListEntryIgnore : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid ListId { get; set; }
    public CuratedList? List { get; set; }
    public Guid TargetUserId { get; set; }
    public User? TargetUser { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
