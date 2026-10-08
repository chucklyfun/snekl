namespace Snekl.Domain.Entities;

public class ForumFlairTemplate : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ForumId { get; set; }
    public Forum? Forum { get; set; }
    public string? Emoji { get; set; }
    public string? IconUrl { get; set; }
    public string? Label { get; set; }
    public string? NameColor { get; set; }
    public string? NameStyle { get; set; }
    public int Position { get; set; }
    public Guid? InheritFromTemplateId { get; set; }
    public ForumFlairTemplate? InheritFromTemplate { get; set; }
}

public class ForumMemberFlair : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ForumId { get; set; }
    public Forum? Forum { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid? TemplateId { get; set; }
    public ForumFlairTemplate? Template { get; set; }
}

public class ForumMemberAlias : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ForumId { get; set; }
    public Forum? Forum { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Alias { get; set; } = string.Empty;
}
