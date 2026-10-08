using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class PostUpvoteServiceTests
{
    [Fact]
    public async Task ToggleAsync_AddsThenRemovesUpvote()
    {
        await using var db = CreateDb();
        var (author, voter, post) = await SeedPostAsync(db);
        var service = new PostUpvoteService(db);

        var up = await service.ToggleAsync(post.Id, voter.Id);
        Assert.True(up.ViewerHasUpvoted);
        Assert.Equal(1, up.UpvoteCount);

        var down = await service.ToggleAsync(post.Id, voter.Id);
        Assert.False(down.ViewerHasUpvoted);
        Assert.Equal(0, down.UpvoteCount);
        Assert.Equal(author.Id, post.AuthorUserId);
    }

    [Fact]
    public async Task ToggleAsync_RejectsSelfUpvote()
    {
        await using var db = CreateDb();
        var (_, _, post) = await SeedPostAsync(db);
        var service = new PostUpvoteService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ToggleAsync(post.Id, post.AuthorUserId));
        Assert.Equal("You cannot upvote your own post.", ex.Message);
        Assert.Equal(0, post.UpvoteCount);
        Assert.Empty(db.PostUpvotes);
    }

    [Fact]
    public async Task ToggleAsync_AllowsRemovingLegacySelfUpvote()
    {
        await using var db = CreateDb();
        var (author, _, post) = await SeedPostAsync(db);
        db.PostUpvotes.Add(new PostUpvote { PostId = post.Id, UserId = author.Id });
        post.UpvoteCount = 1;
        await db.SaveChangesAsync();

        var service = new PostUpvoteService(db);
        var down = await service.ToggleAsync(post.Id, author.Id);
        Assert.False(down.ViewerHasUpvoted);
        Assert.Equal(0, down.UpvoteCount);
    }

    private static async Task<(User Author, User Voter, Post Post)> SeedPostAsync(SneklDbContext db)
    {
        var author = new User
        {
            Username = "alice",
            DisplayName = "Alice",
            Email = "alice@example.com",
            PasswordHash = "x"
        };
        var voter = new User
        {
            Username = "bob",
            DisplayName = "Bob",
            Email = "bob@example.com",
            PasswordHash = "x"
        };
        var forum = new Forum
        {
            Name = "General",
            Slug = "general",
            Description = "",
            CreatedByUserId = Guid.Empty
        };
        db.Users.AddRange(author, voter);
        await db.SaveChangesAsync();
        forum.CreatedByUserId = author.Id;
        db.Forums.Add(forum);
        await db.SaveChangesAsync();

        var post = new Post
        {
            ForumId = forum.Id,
            AuthorUserId = author.Id,
            Message = "hello",
            UpvoteCount = 0
        };
        db.Posts.Add(post);
        await db.SaveChangesAsync();
        return (author, voter, post);
    }

    private static SneklDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SneklDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SneklDbContext(options);
    }
}
