namespace Snekl.Domain.Enums;

public static class FollowAlertModes
{
    public const string Off = "off";
    public const string Posts = "posts";
    public const string Notable = "notable";
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
        if (string.Equals(trimmed, Notable, StringComparison.OrdinalIgnoreCase))
            return Notable;
        if (string.Equals(trimmed, All, StringComparison.OrdinalIgnoreCase))
            return All;

        throw new InvalidOperationException("Follow alerts must be off, posts, notable, or all.");
    }

    public static string NormalizeOrOff(string? mode)
    {
        try
        {
            return Normalize(mode);
        }
        catch (InvalidOperationException)
        {
            return Off;
        }
    }

    public static bool WantsPosts(string mode) => mode is Posts or All;

    public static bool WantsVotes(string mode) => mode is Notable or All;
}
