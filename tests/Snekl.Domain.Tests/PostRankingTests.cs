using Snekl.Domain.Ranking;

namespace Snekl.Domain.Tests;

public class PostRankingTests
{
    [Fact]
    public void HotScore_IncreasesWithUpvotes()
    {
        var now = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
        var created = now.AddHours(-4);

        var low = PostRanking.HotScore(1, created, now);
        var high = PostRanking.HotScore(10, created, now);

        Assert.True(high > low);
    }

    [Fact]
    public void HotScore_DecaysWithAge()
    {
        var now = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);

        var recent = PostRanking.HotScore(10, now.AddHours(-1), now);
        var older = PostRanking.HotScore(10, now.AddHours(-24), now);

        Assert.True(recent > older);
    }

    [Fact]
    public void HotScore_UsesOffsetWhenBrandNew()
    {
        var now = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
        var expected = 5 / Math.Pow(PostRanking.AgeOffsetHours, PostRanking.Gravity);

        Assert.Equal(expected, PostRanking.HotScore(5, now, now), 10);
    }

    [Fact]
    public void HotScore_ClampsFutureCreatedAtToZeroAge()
    {
        var now = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
        var future = now.AddHours(3);
        var expected = 2 / Math.Pow(PostRanking.AgeOffsetHours, PostRanking.Gravity);

        Assert.Equal(expected, PostRanking.HotScore(2, future, now), 10);
    }
}
