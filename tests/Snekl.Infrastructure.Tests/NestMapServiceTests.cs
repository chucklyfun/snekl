using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class NestMapServiceTests
{
    [Fact]
    public void FindByPublicAddress_UsesTopLevelAncestor()
    {
        var town = Guid.NewGuid();
        var block = Guid.NewGuid();
        var general = Guid.NewGuid();
        var nodes = new List<NestRef>
        {
            new(town, null, "town"),
            new(block, town, "block"),
            new(general, block, "general")
        };

        Assert.Equal(general, NestTopology.FindByPublicAddress("general", "town", nodes));
        Assert.Equal(town, NestTopology.FindByPublicAddress("town", null, nodes));
        Assert.Null(NestTopology.FindByPublicAddress("general", "block", nodes));
    }

    [Fact]
    public void NextSlug_SuffixesCollisions()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "general", "general-2" };
        Assert.Equal("general-3", NestTopology.NextSlug("general", taken));
    }

    [Fact]
    public async Task Place_ReparentsRenamesAndRemembersOldAddress()
    {
        await using var db = CreateDb();
        var serverId = Guid.NewGuid();
        var user = Operator(db, serverId);
        var alpha = Forum(db, serverId, "Alpha", "alpha", null);
        var general = Forum(db, serverId, "General", "general", alpha.Id);
        var beta = Forum(db, serverId, "Beta", "beta", null);
        Forum(db, serverId, "General", "general", beta.Id);
        await db.SaveChangesAsync();

        var service = new NestMapService(db, new PermissionService(db), new UnusedForums());
        await service.PlaceAsync(user.Id, general.Id, new PlaceNestRequest(beta.Id, 0));

        var moved = await db.Forums.SingleAsync(f => f.Id == general.Id);
        Assert.Equal(beta.Id, moved.ParentForumId);
        Assert.Equal("general-2", moved.Slug);
        var redirect = await db.ForumRedirects.SingleAsync();
        Assert.Equal(serverId, redirect.ServerId);
        Assert.Equal("alpha", redirect.TopSlug);
        Assert.Equal("general", redirect.Slug);
        Assert.Equal(general.Id, redirect.TargetForumId);
    }

    [Fact]
    public async Task LockedNest_IsVisibleOnlyToOperators()
    {
        await using var db = CreateDb();
        var serverId = Guid.NewGuid();
        var super = Operator(db, serverId);
        var member = new User
        {
            ServerId = serverId,
            Username = "member",
            DisplayName = "Member",
            Email = "member@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(member);
        var forum = Forum(db, serverId, "Town", "town", null);
        forum.Locked = true;
        await db.SaveChangesAsync();

        var permissions = new PermissionService(db);
        Assert.True(await permissions.CanViewForumAsync(super.Id, forum.Id));
        Assert.False(await permissions.CanViewForumAsync(member.Id, forum.Id));
        Assert.False(await permissions.CanSeeForumAsync(null, forum.Id));
    }

    [Fact]
    public async Task Move_LandsLockedAtTopLevelAndCopiesMemberByDid()
    {
        await using var db = CreateDb();
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        db.NodeConfig.Add(Node(sourceId, "did:key:source"));
        db.NodeConfig.Add(Node(destinationId, "did:key:dest"));
        var author = new User
        {
            ServerId = sourceId,
            Username = "ada",
            DisplayName = "Ada",
            Email = "ada@example.com",
            PasswordHash = "x",
            Did = "did:key:ada"
        };
        db.Users.Add(author);
        var forum = Forum(db, sourceId, "Town", "town", null);
        forum.CreatedByUserId = author.Id;
        forum.OwnerUserId = author.Id;
        var child = Forum(db, sourceId, "Block", "block", forum.Id);
        db.ForumMemberships.Add(new ForumMembership { ServerId = sourceId, ForumId = forum.Id, UserId = author.Id });
        db.Posts.Add(new Post
        {
            ServerId = sourceId,
            ForumId = forum.Id,
            AuthorUserId = author.Id,
            Message = "hello",
            Title = "Hello"
        });
        await db.SaveChangesAsync();

        var service = new NestTransferService(db);
        await service.MoveAsync(sourceId, forum.Id, destinationId);

        var moved = await db.Forums.IgnoreQueryFilters().SingleAsync(f => f.Id == forum.Id);
        var movedChild = await db.Forums.IgnoreQueryFilters().SingleAsync(f => f.Id == child.Id);
        Assert.Equal(destinationId, moved.ServerId);
        Assert.Null(moved.ParentForumId);
        Assert.True(moved.Locked);
        Assert.True(movedChild.Locked);
        Assert.Equal(forum.Id, movedChild.ParentForumId);
        var destUser = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.ServerId == destinationId && u.Did == "did:key:ada");
        Assert.Equal(destUser.Id, moved.OwnerUserId);
        var membership = await db.ForumMemberships.IgnoreQueryFilters().SingleAsync(m => m.ForumId == forum.Id);
        Assert.Equal(destinationId, membership.ServerId);
        Assert.Equal(destUser.Id, membership.UserId);
        var post = await db.Posts.IgnoreQueryFilters().SingleAsync(p => p.ForumId == forum.Id);
        Assert.Equal(destUser.Id, post.AuthorUserId);
        var redirect = await db.ForumRedirects.IgnoreQueryFilters().SingleAsync(r => r.Slug == "town");
        Assert.Equal(sourceId, redirect.ServerId);
        Assert.Equal(string.Empty, redirect.TopSlug);
    }

    private static User Operator(SneklDbContext db, Guid serverId)
    {
        var user = new User
        {
            ServerId = serverId,
            Username = "super",
            DisplayName = "Super",
            Email = "super@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        db.SuperUserGrants.Add(new SuperUserGrant { ServerId = serverId, UserId = user.Id });
        return user;
    }

    private static Forum Forum(SneklDbContext db, Guid serverId, string name, string slug, Guid? parentId)
    {
        var forum = new Forum
        {
            ServerId = serverId,
            Name = name,
            Slug = slug,
            Description = "d",
            CreatedByUserId = Guid.NewGuid(),
            ParentForumId = parentId
        };
        db.Forums.Add(forum);
        return forum;
    }

    private static NodeConfig Node(Guid id, string did) => new()
    {
        Id = id,
        Did = did,
        Name = did,
        BaseUrl = "http://localhost"
    };

    private static SneklDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SneklDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SneklDbContext(options);
    }

    private sealed class UnusedForums : IForumService
    {
        public Task<ForumDto> CreateAsync(Guid userId, CreateForumRequest request, Guid? viewerUserId = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<ForumDto> ApproveAsync(Guid userId, Guid forumId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task RejectAsync(Guid userId, Guid forumId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task DeleteAsync(Guid userId, Guid forumId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<ForumDto>> ListAsync(Guid? viewerUserId = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<ForumDto?> GetBySlugAsync(string slug, Guid? viewerUserId = null, string? parentSlug = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<Forum?> GetEntityBySlugAsync(string slug, string? parentSlug = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<ForumDto> JoinAsync(Guid userId, string slug, string? parentSlug = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<ForumDto> LeaveAsync(Guid userId, string slug, string? parentSlug = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<ForumDto> SetFavoriteAsync(Guid userId, string slug, bool isFavorite, string? parentSlug = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task UpsertFederatedAsync(FederatedForumPayload payload, Guid sourceNodeId, string sourceNodeUrl, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
