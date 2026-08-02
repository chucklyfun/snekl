using Snekl.Domain.Enums;

namespace Snekl.Domain.Entities;

public class ForumRole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ForumId { get; set; }
    public Forum? Forum { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public int Position { get; set; }
    public ForumPermissions Permissions { get; set; }
    public bool IsEveryone { get; set; }
    public Guid? InheritFromRoleId { get; set; }
    public ForumRole? InheritFromRole { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ForumRoleAssignment> Assignments { get; set; } = new List<ForumRoleAssignment>();
}

public class ForumRoleAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ForumId { get; set; }
    public Forum? Forum { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid RoleId { get; set; }
    public ForumRole? Role { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}
