using Snekl.Hub.Domain.Dtos;

namespace Snekl.Hub.Infrastructure.Services;

public sealed class OriginRecentThread
{
    public OriginRecentPost? Post { get; set; }
    public string? ForumSlug { get; set; }
    public string? ForumName { get; set; }
    public string? OwnerBaseUrl { get; set; }
    public string? OwnerDid { get; set; }
}

public sealed class OriginRecentPost
{
    public Guid Id { get; set; }
    public Guid ForumId { get; set; }
    public Guid? ParentPostId { get; set; }
    public OriginRecentAuthor? Author { get; set; }
    public string? Message { get; set; }
    public string? Title { get; set; }
    public int ReplyCount { get; set; }
    public int UpvoteCount { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public IReadOnlyList<OriginRecentAttachment>? Attachments { get; set; }
}

public sealed class OriginRecentAuthor
{
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public string? EffectiveDisplayName { get; set; }
}

public sealed class OriginRecentAttachment
{
    public string? Url { get; set; }
    public bool IsNsfw { get; set; }
    public string? CoverText { get; set; }
    public string? CensorUrl { get; set; }
}

public static class HubFeedOriginMapper
{
    public const int ExcerptMax = 180;

    public static string Excerpt(string? message, int max = ExcerptMax)
    {
        var text = (message ?? "").ReplaceLineEndings(" ").Trim();
        if (text.Length <= max) return text;
        return text[..max];
    }

    public static HubFeedCardDto? ToCard(
        OriginRecentThread thread,
        string registeredDid,
        string registeredBaseUrl,
        long mapVersion,
        DateTime eventAt)
    {
        var post = thread.Post;
        if (post is null || post.Id == Guid.Empty || post.ParentPostId is not null || post.IsDeleted)
            return null;

        var ownerDid = (thread.OwnerDid ?? "").Trim();
        if (ownerDid.Length > 0)
        {
            var ownerKeys = FederationRegistry.DidLookupKeys(ownerDid);
            var registeredKeys = FederationRegistry.DidLookupKeys(registeredDid);
            if (!ownerKeys.Intersect(registeredKeys, StringComparer.Ordinal).Any())
                return null;
        }

        var originBase = string.IsNullOrWhiteSpace(thread.OwnerBaseUrl)
            ? registeredBaseUrl.Trim().TrimEnd('/')
            : thread.OwnerBaseUrl.Trim().TrimEnd('/');
        var slug = (thread.ForumSlug ?? "").Trim();
        var author = post.Author;
        var display = author?.EffectiveDisplayName
            ?? author?.DisplayName
            ?? author?.Username
            ?? "";
        var firstImage = post.Attachments?
            .FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(a.CensorUrl) || !string.IsNullOrWhiteSpace(a.Url));
        var image = firstImage is null
            ? null
            : (string.IsNullOrWhiteSpace(firstImage.CensorUrl) ? firstImage.Url : firstImage.CensorUrl);
        var nsfwCover = firstImage?.IsNsfw == true ? firstImage.CoverText : null;

        return new HubFeedCardDto(
            1,
            post.Id,
            post.ForumId,
            slug,
            string.IsNullOrWhiteSpace(thread.ForumName) ? slug : thread.ForumName.Trim(),
            registeredDid,
            originBase,
            $"{originBase}/f/{slug}/{post.Id:D}",
            string.IsNullOrWhiteSpace(post.Title) ? null : post.Title.Trim(),
            Excerpt(post.Message),
            image,
            "",
            display,
            post.CreatedAt,
            post.EditedAt,
            HubFeedService.VisibilityPublic,
            post.UpvoteCount,
            post.ReplyCount,
            mapVersion,
            HubFeedService.EventUpsert,
            eventAt,
            nsfwCover);
    }
}
