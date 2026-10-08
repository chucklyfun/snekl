namespace Snekl.Infrastructure.Services;

public static class HubFeedCards
{
    public const string VisibilityPublic = "public";
    public const string VisibilityMembers = "members";
    public const string EventUpsert = "upsert";
    public const string EventDelete = "delete";
    public const int ExcerptMax = 180;

    public static string Excerpt(string? message, int max = ExcerptMax)
    {
        var text = (message ?? "").ReplaceLineEndings(" ").Trim();
        if (text.Length <= max) return text;
        return text[..max];
    }

    public static string OriginPostUrl(string originBaseUrl, string forumSlug, Guid postId) =>
        $"{originBaseUrl.TrimEnd('/')}/f/{forumSlug}/{postId:D}";
}
