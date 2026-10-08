namespace Snekl.Infrastructure.Search;

public sealed class PostSearchDocument
{
    public string PostId { get; set; } = "";
    public string ThreadRootId { get; set; } = "";
    public string ForumId { get; set; } = "";
    public string? ParentPostId { get; set; }
    public string? Title { get; set; }
    public string Message { get; set; } = "";
    public string AuthorUsername { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed record PostSearchMatch(
    Guid ThreadRootId,
    Guid PostId,
    string Snippet,
    bool MatchedInReply,
    double Score);
