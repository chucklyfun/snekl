using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class PermissionServiceTests
{
    [Fact]
    public async Task GetServerPermissions_UsesEveryoneWhenUnauthenticated()
    {
        await using var db = CreateDb();
        db.ServerRoles.Add(new ServerRole
        {
            Name = "@everyone",
            IsEveryone = true,
            Position = 0,
            Permissions = ServerPermissions.ViewServer
        });
        await db.SaveChangesAsync();

        var service = new PermissionService(db);
        var perms = await service.GetServerPermissionsAsync(null);

        Assert.Equal(ServerPermissions.ViewServer, perms);
    }

    [Fact]
    public async Task GetServerPermissions_MergesAssignedRoles()
    {
        await using var db = CreateDb();
        var user = new User
        {
            Username = "bob",
            DisplayName = "Bob",
            Email = "bob@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        var everyone = new ServerRole
        {
            Name = "@everyone",
            IsEveryone = true,
            Position = 0,
            Permissions = ServerPermissions.ViewServer
        };
        var member = new ServerRole
        {
            Name = "Member",
            Position = 1,
            Permissions = ServerPermissions.CreateForum
        };
        db.ServerRoles.AddRange(everyone, member);
        await db.SaveChangesAsync();
        db.ServerRoleAssignments.Add(new ServerRoleAssignment { UserId = user.Id, RoleId = member.Id });
        await db.SaveChangesAsync();

        var service = new PermissionService(db);
        var perms = await service.GetServerPermissionsAsync(user.Id);

        Assert.True(perms.HasFlag(ServerPermissions.ViewServer));
        Assert.True(perms.HasFlag(ServerPermissions.CreateForum));
    }

    [Fact]
    public async Task IsSuperUser_ReturnsTrueWhenGranted()
    {
        await using var db = CreateDb();
        var user = new User
        {
            Username = "su",
            DisplayName = "SU",
            Email = "su@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.SuperUserGrants.Add(new SuperUserGrant
        {
            UserId = user.Id,
            GrantedBySiteAdminId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();

        var service = new PermissionService(db);
        Assert.True(await service.IsSuperUserAsync(user.Id));
        Assert.False(await service.IsSuperUserAsync(Guid.NewGuid()));
    }

    private static SneklDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SneklDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SneklDbContext(options);
    }
}
