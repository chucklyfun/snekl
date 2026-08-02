namespace Snekl.Domain.Entities;

public class NodeConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Did { get; set; } = string.Empty;
    public string Name { get; set; } = "Snekl Node";
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
