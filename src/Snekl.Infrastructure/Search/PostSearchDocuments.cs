using Snekl.Domain.Entities;

namespace Snekl.Infrastructure.Search;

public static class PostSearchDocuments
{
    public static PostSearchDocument From(Post post, Guid threadRootId, string authorUsername) => new()
    {
        PostId = post.Id.ToString("D"),
        ThreadRootId = threadRootId.ToString("D"),
        ForumId = post.ForumId?.ToString("D") ?? post.WatercoolerId?.ToString("D") ?? "",
        ParentPostId = post.ParentPostId?.ToString("D"),
        Title = post.Title,
        Message = post.IsDeleted ? "" : post.Message,
        AuthorUsername = authorUsername,
        CreatedAt = post.CreatedAt,
        IsDeleted = post.IsDeleted
    };
}
