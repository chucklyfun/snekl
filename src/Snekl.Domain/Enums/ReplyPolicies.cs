namespace Snekl.Domain.Enums;

public static class ReplyPolicies
{
    public const string Everyone = "everyone";
    public const string Following = "following";

    public static bool IsFollowingOnly(string? policy) =>
        string.Equals(Normalize(policy), Following, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? policy)
    {
        if (string.IsNullOrWhiteSpace(policy))
            return Everyone;

        var trimmed = policy.Trim();
        if (string.Equals(trimmed, Everyone, StringComparison.OrdinalIgnoreCase))
            return Everyone;
        if (string.Equals(trimmed, Following, StringComparison.OrdinalIgnoreCase))
            return Following;

        throw new InvalidOperationException("Reply policy must be everyone or following.");
    }
}
