using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class ForumRoleServiceTests
{
    [Fact]
    public async Task UpdateRole_EveryoneKeepsNameAndUpdatesPermissionsAndColor()
    {
        await using var db = CreateDb();
        var forum = AddForum(db);
        var everyone = new ForumRole
        {
            ForumId = forum.Id,
            Name = "@everyone",
            IsEveryone = true,
            Position = 0,
            Color = "#99aab5",
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum |
                ForumPermissions.CreateThread | ForumPermissions.Reply)
        };
        db.ForumRoles.Add(everyone);
        await db.SaveChangesAsync();

        var service = new ForumRoleService(db, new PermissionService(db));
        var updated = await service.UpdateRoleAsync(forum.Id, everyone.Id, new UpdateForumRoleRequest(
            "should-not-apply",
            "#ff0000",
            99,
            ["SeeForum", "ViewForum"]));

        Assert.Equal("@everyone", updated.Name);
        Assert.Equal("#ff0000", updated.Color);
        Assert.Equal(0, updated.Position);
        Assert.True(updated.IsEveryone);
        Assert.Contains("SeeForum", updated.Permissions);
        Assert.Contains("ViewForum", updated.Permissions);
        Assert.DoesNotContain("CreateThread", updated.Permissions);
        Assert.DoesNotContain("Reply", updated.Permissions);
    }

    [Fact]
    public async Task UpdateRole_ModeratorCanChangeNameAndPermissions()
    {
        await using var db = CreateDb();
        var forum = AddForum(db);
        var moderator = new ForumRole
        {
            ForumId = forum.Id,
            Name = "Moderator",
            Position = 50,
            Color = "#43b581",
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum |
                ForumPermissions.CreateThread | ForumPermissions.Reply |
                ForumPermissions.ManagePosts | ForumPermissions.ManageMembers)
        };
        db.ForumRoles.Add(moderator);
        await db.SaveChangesAsync();

        var service = new ForumRoleService(db, new PermissionService(db));
        var updated = await service.UpdateRoleAsync(forum.Id, moderator.Id, new UpdateForumRoleRequest(
            "Staff",
            "#faa61a",
            50,
            ["SeeForum", "ViewForum", "ManagePosts"]));

        Assert.Equal("Staff", updated.Name);
        Assert.Equal("#faa61a", updated.Color);
        Assert.Contains("ManagePosts", updated.Permissions);
        Assert.DoesNotContain("ManageMembers", updated.Permissions);
    }

    [Fact]
    public async Task ListRoles_IncludesForumAdministratorOnOwner()
    {
        await using var db = CreateDb();
        var forum = AddForum(db);
        db.ForumRoles.Add(new ForumRole
        {
            ForumId = forum.Id,
            Name = "Owner",
            Position = 100,
            Modes = PermissionModeMap.FromForumFlags(ForumPermissions.ForumAdministrator)
        });
        await db.SaveChangesAsync();

        var service = new ForumRoleService(db, new PermissionService(db));
        var roles = await service.ListRolesAsync(forum.Id);

        Assert.Contains("ForumAdministrator", roles[0].Permissions);
        Assert.Contains("ManageRoles", roles[0].Permissions);
    }

    [Fact]
    public async Task SeedForumRoles_CreatesMemberRole()
    {
        await using var db = CreateDb();
        var user = new User
        {
            Username = "seed",
            DisplayName = "Seed",
            Email = "seed@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        var forum = AddForum(db);
        await db.SaveChangesAsync();

        var seed = new RoleSeedService(db);
        await seed.SeedForumRolesAsync(forum.Id, user.Id);

        var names = await db.ForumRoles.Where(r => r.ForumId == forum.Id).Select(r => r.Name).ToListAsync();
        Assert.Contains("Member", names);
        Assert.Contains("@everyone", names);
        Assert.Contains("Owner", names);
        Assert.True(await db.ForumRoles.AnyAsync(r => r.ForumId == forum.Id && r.IsOwner));

        var ensured = await seed.EnsureMemberRoleAsync(forum.Id);
        Assert.Equal("Member", ensured.Name);
        Assert.Equal(1, await db.ForumRoles.CountAsync(r => r.ForumId == forum.Id && r.Name == "Member"));
    }

    [Fact]
    public async Task CreateRole_PersistsCatalogPermission()
    {
        await using var db = CreateDb();
        var forum = AddForum(db);
        var type = new ContentBlockType { Key = "calendar", Name = "Calendar", Position = 1 };
        db.ContentBlockTypes.Add(type);
        await db.SaveChangesAsync();
        db.ContentBlockPermissions.Add(new ContentBlockPermission
        {
            BlockTypeId = type.Id,
            Name = "ManageCalendar",
            Label = "Schedule events",
            Scope = ContentBlockScopes.Forum,
            Position = 1
        });
        await db.SaveChangesAsync();

        var service = new ForumRoleService(db, new PermissionService(db));
        var created = await service.CreateRoleAsync(forum.Id, new CreateForumRoleRequest(
            "Hosts",
            "#4f8cff",
            2,
            ["SeeForum", "ViewForum", "ManageCalendar"]));

        Assert.Contains("ManageCalendar", created.Permissions);
        Assert.Contains("SeeContentBlocks", created.Permissions);
    }

    private static Forum AddForum(SneklDbContext db)
    {
        var forum = new Forum
        {
            Name = "General",
            Slug = "general",
            Description = "d",
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
