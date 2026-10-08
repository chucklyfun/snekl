namespace Snekl.Hub.Domain.Entities;

/// <summary>Singleton row tracking the federation map version.</summary>
public class FederationState
{
    public Guid Id { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public long MapVersion { get; set; } = 1;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
