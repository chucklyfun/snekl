namespace Snekl.Domain.Entities;

/// <summary>Opt-in watch for new posts in one nest or one watercooler. Absence means off.</summary>
public class PlaceAlertPreference : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid? ForumId { get; set; }
    public Forum? Forum { get; set; }
    public Guid? WatercoolerId { get; set; }
    public Watercooler? Watercooler { get; set; }
    public string Mode { get; set; } = Enums.PlaceAlertModes.Off;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
