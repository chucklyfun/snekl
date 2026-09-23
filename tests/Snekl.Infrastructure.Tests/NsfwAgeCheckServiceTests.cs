using Microsoft.EntityFrameworkCore;
using Snekl.Domain;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class NsfwAgeCheckServiceTests
{
    [Fact]
    public async Task ServerPolicy_RequiresCheckOnAllForums()
    {
        await using var db = CreateDb();
        var forum = AddForum(db, "art");
        db.NodeConfig.Add(new NodeConfig { NsfwAgeCheckRequired = true, NsfwMinimumAge = 18 });
        await db.SaveChangesAsync();

        var service = new NsfwAgeCheckService(db);
        var status = await service.GetStatusAsync(null, forum.Id);

        Assert.True(status.Enforced);
        Assert.True(status.RequiredByServer);
        Assert.False(status.ViewerAttested);
    }

    [Fact]
    public async Task ForumPolicy_IsEnforcedWithoutServerFlag()
    {
        await using var db = CreateDb();
        var forum = AddForum(db, "nsfw");
        forum.NsfwAgeCheckRequired = true;
        db.NodeConfig.Add(new NodeConfig());
        await db.SaveChangesAsync();

        var service = new NsfwAgeCheckService(db);
        var status = await service.GetStatusAsync(null, forum.Id);

        Assert.True(status.Enforced);
        Assert.True(status.RequiredByForum);
        Assert.False(status.RequiredByServer);
    }

    [Fact]
    public async Task ParentForumPolicy_AppliesToChild()
    {
        await using var db = CreateDb();
        var parent = AddForum(db, "parent");
        parent.NsfwAgeCheckRequired = true;
        var child = AddForum(db, "child");
        child.ParentForumId = parent.Id;
        db.NodeConfig.Add(new NodeConfig());
        await db.SaveChangesAsync();

        var service = new NsfwAgeCheckService(db);
        var status = await service.GetStatusAsync(null, child.Id);

        Assert.True(status.Enforced);
        Assert.True(status.RequiredByForum);
    }

    [Fact]
    public async Task Submit_StoresThresholdWithoutBirthDate()
    {
        await using var db = CreateDb();
        var user = AddUser(db);
        db.NodeConfig.Add(new NodeConfig { NsfwAgeCheckRequired = true, NsfwMinimumAge = 18 });
        await db.SaveChangesAsync();

        var service = new NsfwAgeCheckService(db);
        var dob = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20));
        var status = await service.SubmitAsync(user.Id, dob, null);

        Assert.True(status.ViewerAttested);
        var stored = await db.Users.SingleAsync();
        Assert.NotNull(stored.NsfwAgeAttestedAt);
        Assert.Equal(18, stored.NsfwAgeAttestedMinimum);
    }

    [Fact]
    public async Task Submit_Underage_Throws()
    {
        await using var db = CreateDb();
        var user = AddUser(db);
        db.NodeConfig.Add(new NodeConfig { NsfwAgeCheckRequired = true, NsfwMinimumAge = 18 });
        await db.SaveChangesAsync();

        var service = new NsfwAgeCheckService(db);
        var dob = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-16));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(user.Id, dob, null));
    }

    [Fact]
    public async Task ViewNsfwMedia_IsClippedUntilAttested()
    {
        await using var db = CreateDb();
        var user = AddUser(db);
        var forum = AddForum(db, "art");
        db.NodeConfig.Add(new NodeConfig { NsfwAgeCheckRequired = true, NsfwMinimumAge = 18 });
        db.ServerRoles.Add(new ServerRole
        {
            Name = "@everyone",
            IsEveryone = true,
            Modes = PermissionModeMap.FromServerFlags(ServerPermissions.ViewServer)
        });
        db.ForumRoles.Add(new ForumRole
        {
            ForumId = forum.Id,
            Name = "@everyone",
            IsEveryone = true,
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum | ForumPermissions.ViewNsfwMedia)
        });
        await db.SaveChangesAsync();

        var age = new NsfwAgeCheckService(db);
        var permissions = new PermissionService(db, age);

        var before = await permissions.GetForumPermissionsAsync(user.Id, forum.Id);
        Assert.False(before.HasFlag(ForumPermissions.ViewNsfwMedia));

        await age.SubmitAsync(user.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-21)), forum.Id);

        var after = await permissions.GetForumPermissionsAsync(user.Id, forum.Id);
        Assert.True(after.HasFlag(ForumPermissions.ViewNsfwMedia));
    }

    [Fact]
    public async Task ViewNsfwMedia_IsClippedWithoutPolicyFlags()
    {
        await using var db = CreateDb();
        var user = AddUser(db);
        var forum = AddForum(db, "art");
        db.NodeConfig.Add(new NodeConfig());
        db.ServerRoles.Add(new ServerRole
        {
            Name = "@everyone",
            IsEveryone = true,
            Modes = PermissionModeMap.FromServerFlags(ServerPermissions.ViewServer)
        });
        db.ForumRoles.Add(new ForumRole
        {
            ForumId = forum.Id,
            Name = "@everyone",
            IsEveryone = true,
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum | ForumPermissions.ViewNsfwMedia)
        });
        await db.SaveChangesAsync();

        var age = new NsfwAgeCheckService(db);
        var permissions = new PermissionService(db, age);

        var before = await permissions.GetForumPermissionsAsync(user.Id, forum.Id);
        Assert.False(before.HasFlag(ForumPermissions.ViewNsfwMedia));
        Assert.False((await age.GetStatusAsync(user.Id, forum.Id)).Enforced);
    }

    [Fact]
    public async Task ForumFlag_BlocksEntryUntilAttested()
    {
        await using var db = CreateDb();
        var user = AddUser(db);
        var forum = AddForum(db, "nsfw");
        forum.NsfwAgeCheckRequired = true;
        forum.NsfwAgeCheckExplanation = "Adult drawing workshop.";
        db.NodeConfig.Add(new NodeConfig());
        await db.SaveChangesAsync();

        var service = new NsfwAgeCheckService(db);
        Assert.False(await service.ViewerMayEnterForumAsync(user.Id, forum.Id));

        var status = await service.GetStatusAsync(user.Id, forum.Id);
        Assert.True(status.BlocksEntry);
        Assert.Equal("Adult drawing workshop.", status.ForumExplanation);

        await service.SubmitAsync(user.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-21)), forum.Id);
        Assert.True(await service.ViewerMayEnterForumAsync(user.Id, forum.Id));
        Assert.False((await service.GetStatusAsync(user.Id, forum.Id)).BlocksEntry);
    }

    [Fact]
    public async Task ServerFlag_DoesNotBlockForumEntry()
    {
        await using var db = CreateDb();
        var user = AddUser(db);
        var forum = AddForum(db, "art");
        db.NodeConfig.Add(new NodeConfig { NsfwAgeCheckRequired = true, NsfwMinimumAge = 18 });
        await db.SaveChangesAsync();

        var service = new NsfwAgeCheckService(db);
        Assert.True(await service.ViewerMayEnterForumAsync(user.Id, forum.Id));
        Assert.False((await service.GetStatusAsync(user.Id, forum.Id)).BlocksEntry);
    }

    private static User AddUser(SneklDbContext db)
    {
        var user = new User
        {
            Username = "pat",
            DisplayName = "Pat",
            Email = "pat@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        return user;
    }

    private static Forum AddForum(SneklDbContext db, string slug)
    {
        var forum = new Forum
        {
            Id = Guid.NewGuid(),
            Name = slug,
            Slug = slug,
            Description = "",
            CreatedByUserId = Guid.NewGuid()
        };
        db.Forums.Add(forum);
        return forum;
    }

    private static SneklDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SneklDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SneklDbContext(options);
    }
}
