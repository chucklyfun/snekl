namespace Snekl.Domain.Entities;

public class Forum : ExternalEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public User? CreatedBy { get; set; }
    public Guid? ParentForumId { get; set; }
    public Forum? ParentForum { get; set; }
    public bool AllowAliases { get; set; } = true;

    public ICollection<Post> Posts { get; set; } = new List<Post>();
    public ICollection<Forum> Subforums { get; set; } = new List<Forum>();
    public ICollection<ForumRole> Roles { get; set; } = new List<ForumRole>();
    public ICollection<ForumFlairTemplate> FlairTemplates { get; set; } = new List<ForumFlairTemplate>();
}
