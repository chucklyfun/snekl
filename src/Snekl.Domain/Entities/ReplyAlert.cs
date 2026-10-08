namespace Snekl.Domain.Entities;

/// <summary>
/// One burst of replies on a thread. Stays open until the recipient opens it, then the next reply starts a new row.
/// </summary>
public class ReplyAlert : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecipientUserId { get; set; }
    public User? Recipient { get; set; }
    public string Kind { get; set; } = Enums.ReplyAlertKinds.Thread;
    public Guid? RootPostId { get; set; }
    public Post? RootPost { get; set; }
    public Guid? ForumId { get; set; }
    public Guid? WatercoolerId { get; set; }

    /// <summary>Person a follow alert is about.</summary>
    public Guid? SubjectUserId { get; set; }
    public int ReplyCount { get; set; }
    public Guid LatestReplyId { get; set; }
    public DateTime LatestReplyAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}
