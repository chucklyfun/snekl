using Snekl.Domain.Enums;

namespace Snekl.Domain.Entities;

public class Post : ExternalEntity, IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid? ForumId { get; set; }
    public Forum? Forum { get; set; }
    public Guid? WatercoolerId { get; set; }
    public Watercooler? Watercooler { get; set; }
    public Guid? ParentPostId { get; set; }
    public Post? ParentPost { get; set; }
    public Guid? QuotedPostId { get; set; }
    public Post? QuotedPost { get; set; }
    public int QuoteCount { get; set; }
    public Guid AuthorUserId { get; set; }
    public User? Author { get; set; }
    public Guid? AuthorPersonClusterId { get; set; }
    public string? AuthorDid { get; set; }

    /// <summary>Thread title for root posts only; null for replies.</summary>
    public string? Title { get; set; }

    public string Message { get; set; } = string.Empty;

    public ICollection<PostMedia> Media { get; set; } = new List<PostMedia>();

    /// <summary>Total number of descendant replies in this post's subtree (not direct-only).</summary>
    public int ReplyCount { get; set; }

    public int UpvoteCount { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public DateTime? EditedAt { get; set; }

    public bool IsLocked { get; set; }

    public bool IsPinned { get; set; }

    /// <summary>
    /// Root posts only. <see cref="ReplyPolicies.Everyone"/> (default) or
    /// <see cref="ReplyPolicies.Following"/> (people the original poster follows).
    /// </summary>
    public string ReplyPolicy { get; set; } = ReplyPolicies.Everyone;

    public DateTime? LockedAt { get; set; }

    public DateTime? PinnedAt { get; set; }

    public ICollection<PostUpvote> Upvotes { get; set; } = new List<PostUpvote>();

    public ICollection<PostReaction> Reactions { get; set; } = new List<PostReaction>();

    public ICollection<PostRevision> Revisions { get; set; } = new List<PostRevision>();

    public ICollection<Post> Replies { get; set; } = new List<Post>();

    public bool IsLocallyOwned => !OriginNodeId.HasValue;
}
