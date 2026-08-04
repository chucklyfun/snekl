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
        var user = new User
        {
            Username = "alice",
            DisplayName = "Alice",
            Email = "alice@example.com",
            PasswordHash = "x"
        };
        var forum = new Forum
        {
            Name = "General",
            Slug = "general",
            Description = "",
            CreatedByUserId = Guid.Empty
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        forum.CreatedByUserId = user.Id;
        db.Forums.Add(forum);
        await db.SaveChangesAsync();

        var post = new Post
        {
            ForumId = forum.Id,
            AuthorUserId = user.Id,
            Message = "hello",
            UpvoteCount = 0
        };
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var service = new PostUpvoteService(db);

        var up = await service.ToggleAsync(post.Id, user.Id);
        Assert.True(up.ViewerHasUpvoted);
        Assert.Equal(1, up.UpvoteCount);

        var down = await service.ToggleAsync(post.Id, user.Id);
        Assert.False(down.ViewerHasUpvoted);
        Assert.Equal(0, down.UpvoteCount);
    }

    private static SneklDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SneklDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SneklDbContext(options);
    }
}
