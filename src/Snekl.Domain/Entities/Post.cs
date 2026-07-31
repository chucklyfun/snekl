namespace Snekl.Domain.Entities;

public class Post : ExternalEntity
{
    public Guid ForumId { get; set; }
    public Forum? Forum { get; set; }
    public Guid? ParentPostId { get; set; }
    public Post? ParentPost { get; set; }
    public Guid AuthorUserId { get; set; }
    public User? Author { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ReplyCount { get; set; }

    public ICollection<Post> Replies { get; set; } = new List<Post>();
}
