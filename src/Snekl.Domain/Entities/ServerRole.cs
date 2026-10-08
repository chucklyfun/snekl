using Snekl.Domain.Enums;

namespace Snekl.Domain.Entities;

public class ServerRole : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public int Position { get; set; }

    /// <summary>Permission → GrantMode. Missing key is None.</summary>
    public Dictionary<string, GrantMode> Modes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEveryone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ServerRoleAssignment> Assignments { get; set; } = new List<ServerRoleAssignment>();
}

public class ServerRoleAssignment : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid RoleId { get; set; }
    public ServerRole? Role { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}
