namespace Snekl.Hub.Domain.Ranking;

/// <summary>
/// Same formula as Snekl.Domain.Ranking.PostRanking. Keep constants in sync.
/// </summary>
public static class PostRanking
{
    public const double Gravity = 1.5;
    public const double AgeOffsetHours = 2.0;

    public static double HotScore(int upvoteCount, DateTime createdAt, DateTime? now = null)
    {
        var reference = now ?? DateTime.UtcNow;
        var ageHours = Math.Max((reference - createdAt).TotalHours, 0);
        return upvoteCount / Math.Pow(ageHours + AgeOffsetHours, Gravity);
    }
}
