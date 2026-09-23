using Snekl.Domain.Enums;

namespace Snekl.Domain.Tests;

public class ReplyPoliciesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_Blank_IsEveryone(string? policy)
    {
        Assert.Equal(ReplyPolicies.Everyone, ReplyPolicies.Normalize(policy));
        Assert.False(ReplyPolicies.IsFollowingOnly(policy));
    }

    [Theory]
    [InlineData("everyone")]
    [InlineData("Everyone")]
    [InlineData("EVERYONE")]
    public void Normalize_Everyone_IsCaseInsensitive(string policy)
    {
        Assert.Equal(ReplyPolicies.Everyone, ReplyPolicies.Normalize(policy));
    }

    [Theory]
    [InlineData("following")]
    [InlineData("Following")]
    public void Normalize_Following_IsCaseInsensitive(string policy)
    {
        Assert.Equal(ReplyPolicies.Following, ReplyPolicies.Normalize(policy));
        Assert.True(ReplyPolicies.IsFollowingOnly(policy));
    }

    [Fact]
    public void Normalize_Unknown_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ReplyPolicies.Normalize("friends"));
    }
}
