namespace Snekl.Domain.Entities;

public static class DirectConversationRequestStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Declined = "declined";
    public const string Cancelled = "cancelled";
    public const string Ignored = "ignored";
}

public static class DirectMessageForumPreferenceModes
{
    public const string None = "none";
    public const string Ignore = "ignore";
    public const string Reject = "reject";

    public static bool IsStored(string? mode) =>
        string.Equals(mode, Ignore, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mode, Reject, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? mode)
    {
        if (string.Equals(mode, Ignore, StringComparison.OrdinalIgnoreCase)) return Ignore;
        if (string.Equals(mode, Reject, StringComparison.OrdinalIgnoreCase)) return Reject;
        if (string.IsNullOrWhiteSpace(mode) ||
            string.Equals(mode, None, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "accept", StringComparison.OrdinalIgnoreCase))
        {
            return None;
        }

        throw new InvalidOperationException("Nest request handling must be accept, ignore, or reject.");
    }
}

/// <summary>
/// Follow-back request so two people can open a DM. Gated by
/// <see cref="Enums.ForumPermissions.RequestDirectMessage"/> on a shared forum.
/// </summary>
public class DirectConversationRequest : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequesterUserId { get; set; }
    public User? Requester { get; set; }
    public Guid TargetUserId { get; set; }
    public User? Target { get; set; }
    public Guid ForumId { get; set; }
    public Forum? Forum { get; set; }
    public string? Message { get; set; }
    public string Status { get; set; } = DirectConversationRequestStatuses.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public Guid? ConversationId { get; set; }
    public DirectConversation? Conversation { get; set; }
}

public class DirectConversation : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserLowId { get; set; }
    public User? UserLow { get; set; }
    public Guid UserHighId { get; set; }
    public User? UserHigh { get; set; }
    public Guid? OriginForumId { get; set; }
    public Forum? OriginForum { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<DirectMessage> Messages { get; set; } = new List<DirectMessage>();
}

public class DirectMessage : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public DirectConversation? Conversation { get; set; }
    public Guid AuthorUserId { get; set; }
    public User? Author { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Per-user handling of follow-back / DM requests that cite a given forum.
/// Missing row means accept (show in inbox).
/// </summary>
public class DirectMessageForumPreference : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid ForumId { get; set; }
    public Forum? Forum { get; set; }
    /// <summary><see cref="DirectMessageForumPreferenceModes.Ignore"/> or <see cref="DirectMessageForumPreferenceModes.Reject"/>.</summary>
    public string Mode { get; set; } = DirectMessageForumPreferenceModes.Ignore;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
