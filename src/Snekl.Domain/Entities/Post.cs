namespace Snekl.Domain.Entities;

public class Post : ExternalEntity
{
    public Guid ForumId { get; set; }
    public Forum? Forum { get; set; }
    public Guid? ParentPostId { get; set; }
    public Post? ParentPost { get; set; }
    public Guid AuthorUserId { get; set; }
    public User? Author { get; set; }
    public Guid? AuthorPersonClusterId { get; set; }
    public string? AuthorDid { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>Total number of descendant replies in this post's subtree (not direct-only).</summary>
    public int ReplyCount { get; set; }

    public int UpvoteCount { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public DateTime? EditedAt { get; set; }

    public bool IsLocked { get; set; }

    public bool IsPinned { get; set; }

    public DateTime? LockedAt { get; set; }

    public DateTime? PinnedAt { get; set; }

    public ICollection<PostUpvote> Upvotes { get; set; } = new List<PostUpvote>();

    public ICollection<PostRevision> Revisions { get; set; } = new List<PostRevision>();

    public ICollection<Post> Replies { get; set; } = new List<Post>();

    public bool IsLocallyOwned => !OriginNodeId.HasValue;
}
