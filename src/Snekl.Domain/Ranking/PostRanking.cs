namespace Snekl.Domain.Ranking;

/// <summary>
/// Server-agnostic post ranking. Safe to replicate in client apps when merging posts from multiple content servers.
/// </summary>
public static class PostRanking
{
    public const double Gravity = 1.5;
    public const double AgeOffsetHours = 2.0;

    /// <summary>
    /// Hot score: upvotes divided by (age + offset) raised to gravity. Higher ranks first.
    /// </summary>
    public static double HotScore(int upvoteCount, DateTime createdAt, DateTime? now = null)
    {
        var reference = now ?? DateTime.UtcNow;
        var ageHours = Math.Max((reference - createdAt).TotalHours, 0);
        return upvoteCount / Math.Pow(ageHours + AgeOffsetHours, Gravity);
    }
}
