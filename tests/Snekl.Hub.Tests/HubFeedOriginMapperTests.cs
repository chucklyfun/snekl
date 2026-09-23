using Snekl.Hub.Infrastructure.Services;

namespace Snekl.Hub.Tests;

public class HubFeedOriginMapperTests
{
    [Fact]
    public void ToCard_MapsPublicRootThread()
    {
        var postId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var forumId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
        var thread = new OriginRecentThread
        {
            ForumSlug = "garden",
            ForumName = "Garden",
            OwnerBaseUrl = "http://alpha.test/",
            OwnerDid = "did:web:alpha.test",
            Post = new OriginRecentPost
            {
                Id = postId,
                ForumId = forumId,
                Title = "Roses",
                Message = "  hello\nworld  ",
                UpvoteCount = 4,
                ReplyCount = 1,
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                Author = new OriginRecentAuthor { DisplayName = "Alice" },
                Attachments = [new OriginRecentAttachment { Url = "http://alpha.test/img.png" }]
            }
        };

        var eventAt = DateTime.UtcNow;
        var card = HubFeedOriginMapper.ToCard(thread, "did:web:alpha.test", "http://alpha.test", 3, eventAt);

        Assert.NotNull(card);
        Assert.Equal(postId, card.PostId);
        Assert.Equal("did:web:alpha.test", card.ServerDid);
        Assert.Equal("http://alpha.test/f/garden/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", card.OriginPostUrl);
        Assert.Equal("hello world", card.Excerpt);
        Assert.Equal(4, card.Score);
        Assert.Equal("public", card.Visibility);
        Assert.Equal("Alice", card.AuthorDisplayName);
        Assert.Equal("http://alpha.test/img.png", card.ImagePreviewUrl);
        Assert.Null(card.NsfwCover);
    }

    [Fact]
    public void ToCard_UsesCensorUrlForNsfwAttachments()
    {
        var thread = new OriginRecentThread
        {
            ForumSlug = "garden",
            ForumName = "Garden",
            OwnerBaseUrl = "http://alpha.test/",
            OwnerDid = "did:web:alpha.test",
            Post = new OriginRecentPost
            {
                Id = Guid.NewGuid(),
                ForumId = Guid.NewGuid(),
                Title = "Hidden",
                Message = "body",
                CreatedAt = DateTime.UtcNow,
                Attachments =
                [
                    new OriginRecentAttachment
                    {
                        Url = null,
                        IsNsfw = true,
                        CoverText = "A covered photo.",
                        CensorUrl = "http://alpha.test/api/media/x/preview"
                    }
                ]
            }
        };

        var card = HubFeedOriginMapper.ToCard(thread, "did:web:alpha.test", "http://alpha.test", 1, DateTime.UtcNow);
        Assert.NotNull(card);
        Assert.Equal("http://alpha.test/api/media/x/preview", card.ImagePreviewUrl);
        Assert.Equal("A covered photo.", card.NsfwCover);
    }

    [Fact]
    public void ToCard_SkipsRepliesDeletesAndMismatchedDid()
    {
        Assert.Null(HubFeedOriginMapper.ToCard(
            new OriginRecentThread
            {
                Post = new OriginRecentPost { Id = Guid.NewGuid(), ForumId = Guid.NewGuid(), ParentPostId = Guid.NewGuid() }
            },
            "did:web:a.test",
            "http://a.test",
            1,
            DateTime.UtcNow));
        Assert.Null(HubFeedOriginMapper.ToCard(
            new OriginRecentThread
            {
                Post = new OriginRecentPost { Id = Guid.NewGuid(), ForumId = Guid.NewGuid(), IsDeleted = true }
            },
            "did:web:a.test",
            "http://a.test",
            1,
            DateTime.UtcNow));
        Assert.Null(HubFeedOriginMapper.ToCard(
            new OriginRecentThread
            {
                OwnerDid = "did:web:other.test",
                Post = new OriginRecentPost { Id = Guid.NewGuid(), ForumId = Guid.NewGuid() }
            },
            "did:web:a.test",
            "http://a.test",
            1,
            DateTime.UtcNow));
    }
}
