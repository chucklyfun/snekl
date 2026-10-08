namespace Snekl.Domain.Entities;

public class ForumPage : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ForumId { get; set; }
    public Forum? Forum { get; set; }
    public string Title { get; set; } = "Home";
    public string Slug { get; set; } = ForumPageSlugs.Home;
    public bool IsDefault { get; set; } = true;
    /// <summary>Locked shared layout applied around every page of this forum (contains a page-body slot).</summary>
    public bool IsChrome { get; set; }
    /// <summary>Validated region tree JSON (see ForumPageLayout).</summary>
    public string LayoutJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class ForumPageSlugs
{
    public const string Home = "home";
    /// <summary>Reserved slug for locked shared sections applied to every page of this forum.</summary>
    public const string Chrome = "chrome";

    public static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        Home,
        Chrome,
        "settings",
        "p",
        "admin",
        "members",
        "roles",
        "emotes",
        "pages"
    };
}

public static class ForumPageViewTypes
{
    public const string Stack = "stack";
    public const string Columns = "columns";
    public const string Grid = "grid";
    public const string ForumHeader = "forum-header";
    public const string ForumView = "forum-view";
    public const string WatercoolerView = "watercooler-view";
    public const string CoolerList = "cooler-list";
    public const string SubforumGrid = "subforum-grid";
    /// <summary>Slot in shared chrome where each page’s layout is injected. Exactly one required in chrome.</summary>
    public const string PageBody = "page-body";
    public const string Extension = "extension";

    public static readonly HashSet<string> Containers = new(StringComparer.OrdinalIgnoreCase)
    {
        Stack,
        Columns,
        Grid
    };

    public static readonly HashSet<string> Leaves = new(StringComparer.OrdinalIgnoreCase)
    {
        ForumHeader,
        ForumView,
        WatercoolerView,
        CoolerList,
        SubforumGrid,
        PageBody,
        Extension
    };

    public static readonly HashSet<string> All = new(
        Containers.Concat(Leaves),
        StringComparer.OrdinalIgnoreCase);
}
