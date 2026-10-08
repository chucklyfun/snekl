namespace Snekl.Domain.Enums;

public static class PlaceAlertModes
{
    public const string Off = "off";
    public const string Posts = "posts";
    public const string All = "all";

    public static string Normalize(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return Off;

        var trimmed = mode.Trim();
        if (string.Equals(trimmed, Off, StringComparison.OrdinalIgnoreCase))
            return Off;
        if (string.Equals(trimmed, Posts, StringComparison.OrdinalIgnoreCase))
            return Posts;
        if (string.Equals(trimmed, All, StringComparison.OrdinalIgnoreCase))
            return All;

        throw new InvalidOperationException("Place alerts must be off, posts, or all.");
    }
}

public static class ReplyAlertKinds
{
    public const string Thread = "thread";
    public const string Nest = "nest";
    public const string Watercooler = "watercooler";
    public const string Vote = "vote";
    public const string Follow = "follow";
}
