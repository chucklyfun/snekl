using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class HubFeedCardsTests
{
    [Fact]
    public void Excerpt_TrimsAndCapsLength()
    {
        Assert.Equal("hello", HubFeedCards.Excerpt("  hello\n"));
        var longText = new string('a', 200);
        Assert.Equal(180, HubFeedCards.Excerpt(longText).Length);
    }

    [Fact]
    public void OriginPostUrl_UsesForumSlugAndPostId()
    {
        var postId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        Assert.Equal(
            "http://alpha.test/f/garden/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            HubFeedCards.OriginPostUrl("http://alpha.test/", "garden", postId));
    }
}
