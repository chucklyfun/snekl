using Snekl.Domain.Enums;

namespace Snekl.Domain.Entities;

public class Watercooler : ExternalEntity, IServerScoped
{
    public Guid ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public User? CreatedBy { get; set; }
    public Guid? ParentForumId { get; set; }
    public Forum? ParentForum { get; set; }
    public string ApprovalStatus { get; set; } = ForumApprovalStatuses.Approved;
    public bool AllowUpvotes { get; set; }
    public bool NewestAtBottom { get; set; } = true;

    public ICollection<Post> Posts { get; set; } = new List<Post>();
    public ICollection<WatercoolerRole> Roles { get; set; } = new List<WatercoolerRole>();
}

public class WatercoolerRole : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WatercoolerId { get; set; }
    public Watercooler? Watercooler { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public int Position { get; set; }
    public Dictionary<string, GrantMode> Modes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsEveryone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<WatercoolerRoleAssignment> Assignments { get; set; } = new List<WatercoolerRoleAssignment>();
}

public class WatercoolerRoleAssignment : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WatercoolerId { get; set; }
    public Watercooler? Watercooler { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid RoleId { get; set; }
    public WatercoolerRole? Role { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}

public class ForumContentBlock : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ForumId { get; set; }
    public Forum? Forum { get; set; }
    public string TypeKey { get; set; } = string.Empty;
    public Guid? WatercoolerId { get; set; }
    public Watercooler? Watercooler { get; set; }
    public string Slot { get; set; } = ContentBlockSlots.Main;
    public int Position { get; set; }
}

public static class ContentBlockSlots
{
    public const string Main = "main";
    public const string Sidebar = "sidebar";
}

public static class WatercoolerBlockTypes
{
    public const string CoolerList = "cooler-list";
    public const string CoolerFeed = "cooler-feed";
    public const string WatercoolerView = "watercooler-view";
}
