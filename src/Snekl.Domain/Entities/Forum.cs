namespace Snekl.Domain.Entities;

public class Forum : ExternalEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public User? CreatedBy { get; set; }

    public ICollection<Post> Posts { get; set; } = new List<Post>();
}
