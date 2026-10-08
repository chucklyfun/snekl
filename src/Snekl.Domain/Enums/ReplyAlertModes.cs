namespace Snekl.Domain.Enums;

public static class ReplyAlertModes
{
    public const string Replies = "replies";
    public const string All = "all";
    public const string Off = "off";

    public static string Normalize(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return Replies;

        var trimmed = mode.Trim();
        if (string.Equals(trimmed, Replies, StringComparison.OrdinalIgnoreCase))
            return Replies;
        if (string.Equals(trimmed, All, StringComparison.OrdinalIgnoreCase))
            return All;
        if (string.Equals(trimmed, Off, StringComparison.OrdinalIgnoreCase))
            return Off;

        throw new InvalidOperationException("Reply alerts must be replies, all, or off.");
    }
}
