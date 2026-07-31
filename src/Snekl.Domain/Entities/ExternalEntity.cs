namespace Snekl.Domain.Entities;

public abstract class ExternalEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid? OriginNodeId { get; set; }
    public string? OriginNodeUrl { get; set; }
    public bool IsFederated => OriginNodeId.HasValue;
}
