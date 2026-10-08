using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;
using Snekl.Redis;

namespace Snekl.Infrastructure.Tests;

public class OwnershipServiceTests
{
    [Fact]
    public async Task ClaimingServer_SetsServerOwner()
    {
        await using var db = CreateDb();
        db.NodeConfig.Add(new NodeConfig { Name = "Test", BaseUrl = "http://localhost:5001" });
        var user = AddUser(db, "alice");
        await db.SaveChangesAsync();

        var service = new SiteAdminAuthService(db, new IndexConfig());
        await service.ElevateAsync(user.Id, new OperatorLoginRequest(NewAdminPassword: "Operator1!"));

        Assert.Equal(user.Id, await db.NodeConfig.Select(n => n.OwnerUserId).FirstAsync());
    }

    [Fact]
    public async Task SuperUserRevoke_RefusesServerOwner()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var other = AddUser(db, "other");
        db.NodeConfig.Add(new NodeConfig { OwnerUserId = owner.Id });
        db.SuperUserGrants.Add(new SuperUserGrant { UserId = owner.Id });
        db.SuperUserGrants.Add(new SuperUserGrant { UserId = other.Id });
        await db.SaveChangesAsync();

        var permissions = new PermissionService(db);
        var supers = new SuperUserService(db, permissions);

        await Assert.ThrowsAsync<InvalidOperationException>(() => supers.RevokeAsync(owner.Id));
        await supers.RevokeAsync(other.Id);
        Assert.False(await db.SuperUserGrants.AnyAsync(g => g.UserId == other.Id));
    }

    [Fact]
    public async Task OperatorRevoke_RefusesServerOwner()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var other = AddUser(db, "other");
        db.NodeConfig.Add(new NodeConfig { OwnerUserId = owner.Id });
        var ownerAdmin = new SiteAdmin { UserId = owner.Id, Username = owner.Username, PasswordHash = "x" };
        var otherAdmin = new SiteAdmin { UserId = other.Id, Username = other.Username, PasswordHash = "x" };
        db.SiteAdmins.AddRange(ownerAdmin, otherAdmin);
        await db.SaveChangesAsync();

        var service = new SiteAdminAuthService(db, new IndexConfig());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RevokeAsync(ownerAdmin.Id));
        await service.RevokeAsync(otherAdmin.Id);
        Assert.Equal(1, await db.SiteAdmins.CountAsync());
    }

    [Fact]
    public async Task ServerOwner_GetsAdministratorPermissions()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        db.NodeConfig.Add(new NodeConfig { OwnerUserId = owner.Id });
        db.ServerRoles.Add(new ServerRole
        {
            Name = "@everyone",
            IsEveryone = true,
            Modes = PermissionModeMap.FromServerFlags(ServerPermissions.ViewServer)
        });
        await db.SaveChangesAsync();

        var permissions = new PermissionService(db);
        Assert.True(await permissions.IsServerOwnerAsync(owner.Id));
        var perms = await permissions.GetServerPermissionsAsync(owner.Id);
        Assert.True(perms.HasFlag(ServerPermissions.ServerAdministrator));
    }

    [Fact]
    public async Task TransferServerOwnership_RequiresOwnerAndLocalUser()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var next = AddUser(db, "next");
        var stranger = AddUser(db, "stranger");
        db.NodeConfig.Add(new NodeConfig
        {
            Name = "Test",
            BaseUrl = "http://localhost:5001",
            OwnerUserId = owner.Id
        });
        await db.SaveChangesAsync();

        var ownership = CreateOwnership(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ownership.TransferServerOwnershipAsync(stranger.Id, "next"));
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ownership.TransferServerOwnershipAsync(owner.Id, "@ghost"));
        Assert.Contains("refresh their login from the auth server", missing.Message);

        var info = await ownership.TransferServerOwnershipAsync(owner.Id, "@Next");
        Assert.Equal(next.Id, await db.NodeConfig.Select(n => n.OwnerUserId).FirstAsync());
        Assert.Equal("next", info.OwnerUsername);
        Assert.False(await new PermissionService(db).IsServerOwnerAsync(owner.Id));
        Assert.True(await new PermissionService(db).IsServerOwnerAsync(next.Id));
    }

    [Fact]
    public async Task ForumOwner_PeerAdminCannotStripOwnerRole_ServerAdminCan()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var peer = AddUser(db, "peer");
        var serverAdmin = AddUser(db, "admin");
        var forum = new Forum
        {
            Name = "General",
            Slug = "general",
            Description = "",
            CreatedByUserId = owner.Id,
            OwnerUserId = owner.Id
        };
        db.Forums.Add(forum);
        await db.SaveChangesAsync();

        var seed = new RoleSeedService(db);
        await seed.SeedForumRolesAsync(forum.Id, owner.Id);
        var ownerRole = await db.ForumRoles.SingleAsync(r => r.ForumId == forum.Id && r.IsOwner);
        var memberRole = await db.ForumRoles.SingleAsync(r => r.ForumId == forum.Id && r.Name == "Member");
        db.ForumRoleAssignments.Add(new ForumRoleAssignment
        {
            ForumId = forum.Id,
            UserId = peer.Id,
            RoleId = ownerRole.Id
        });
        var adminRole = new ServerRole
        {
            Name = "Admin",
            Position = 10,
            Modes = PermissionModeMap.FromServerFlags(ServerPermissions.ServerAdministrator)
        };
        db.ServerRoles.Add(adminRole);
        await db.SaveChangesAsync();
        db.ServerRoleAssignments.Add(new ServerRoleAssignment { UserId = serverAdmin.Id, RoleId = adminRole.Id });
        await db.SaveChangesAsync();

        var roles = new ForumRoleService(db, new PermissionService(db));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            roles.AssignRolesAsync(forum.Id, owner.Id, [memberRole.Id], peer.Id));

        await roles.AssignRolesAsync(forum.Id, owner.Id, [memberRole.Id], serverAdmin.Id);
        Assert.Null((await db.Forums.SingleAsync(f => f.Id == forum.Id)).OwnerUserId);
        Assert.False(await db.ForumRoleAssignments.AnyAsync(a =>
            a.ForumId == forum.Id && a.UserId == owner.Id && a.RoleId == ownerRole.Id));
    }

    [Fact]
    public async Task TransferForumOwnership_MovesOwnerRole()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var next = AddUser(db, "next");
        var forum = new Forum
        {
            Name = "General",
            Slug = "general",
            Description = "",
            CreatedByUserId = owner.Id,
            OwnerUserId = owner.Id
        };
        db.Forums.Add(forum);
        await db.SaveChangesAsync();
        await new RoleSeedService(db).SeedForumRolesAsync(forum.Id, owner.Id);

        var ownership = CreateOwnership(db);
        await ownership.TransferForumOwnershipAsync(forum.Id, owner.Id, "next");

        var updated = await db.Forums.SingleAsync(f => f.Id == forum.Id);
        Assert.Equal(next.Id, updated.OwnerUserId);
        var ownerRoleId = await db.ForumRoles
            .Where(r => r.ForumId == forum.Id && r.IsOwner)
            .Select(r => r.Id)
            .SingleAsync();
        Assert.True(await db.ForumRoleAssignments.AnyAsync(a =>
            a.ForumId == forum.Id && a.UserId == next.Id && a.RoleId == ownerRoleId));
        Assert.False(await db.ForumRoleAssignments.AnyAsync(a =>
            a.ForumId == forum.Id && a.UserId == owner.Id && a.RoleId == ownerRoleId));
    }

    private static OwnershipService CreateOwnership(SneklDbContext db)
    {
        var permissions = new PermissionService(db);
        var nodes = new NodeService(
            db,
            Options.Create(new RateLimitOptions()),
            new IndexConfig());
        return new OwnershipService(db, permissions, new RoleSeedService(db), nodes);
    }

    private static User AddUser(SneklDbContext db, string username)
    {
        var user = new User
        {
            Username = username,
            DisplayName = username,
            Email = $"{username}@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        return user;
    }

    private static SneklDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<SneklDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class IndexConfig : IConfiguration
    {
        public string? this[string key]
        {
            get => key switch
            {
                "Jwt:Key" => "unit-test-jwt-key-that-is-long-enough",
                "Jwt:Issuer" => "snekl-test",
                "Admin:TokenExpiryHours" => "8",
                "Admin:GrantOperatorsSuperUser" => "true",
                _ => null
            };
            set { }
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => new NoopChangeToken();
        public IConfigurationSection GetSection(string key) => new EmptySection(key);

        private sealed class EmptySection : IConfigurationSection
        {
            private readonly string _key;

            public EmptySection(string key) => _key = key;

            public string? this[string childKey]
            {
                get => null;
                set { }
            }

            public string Key => _key;
            public string Path => _key;
            public string? Value
            {
                get => null;
                set { }
            }

            public IEnumerable<IConfigurationSection> GetChildren() => [];
            public IChangeToken GetReloadToken() => new NoopChangeToken();
            public IConfigurationSection GetSection(string childKey) => new EmptySection($"{_key}:{childKey}");
        }
    }

    private sealed class NoopChangeToken : IChangeToken
    {
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) =>
            new NoopDisposable();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
