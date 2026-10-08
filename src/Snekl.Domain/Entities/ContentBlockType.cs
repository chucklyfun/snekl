namespace Snekl.Domain.Entities;

public static class ContentBlockScopes
{
    public const string Forum = "forum";
    public const string Server = "server";
}

public class ContentBlockType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int Position { get; set; }
    public ICollection<ContentBlockPermission> Permissions { get; set; } = new List<ContentBlockPermission>();
}

public class ContentBlockPermission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BlockTypeId { get; set; }
    public ContentBlockType? BlockType { get; set; }
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    public string Scope { get; set; } = ContentBlockScopes.Forum;
    public int Position { get; set; }
}
