using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class SiteAdminAuthServiceTests
{
    [Fact]
    public async Task ClaimingServer_AlsoGrantsSuperUser()
    {
        await using var db = CreateDb();
        db.NodeConfig.Add(new NodeConfig { Name = "Test", BaseUrl = "http://localhost:5001" });
        var user = AddUser(db, "alice");
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.ElevateAsync(user.Id, new OperatorLoginRequest(NewAdminPassword: "Operator1!"));

        Assert.True(await db.SuperUserGrants.AnyAsync(g => g.UserId == user.Id));
        Assert.Equal(user.Id, await db.NodeConfig.Select(n => n.OwnerUserId).SingleAsync());
    }

    [Fact]
    public async Task InvitingOperator_AlsoGrantsSuperUser()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var invited = AddUser(db, "bob");
        var ownerAdmin = new SiteAdmin
        {
            UserId = owner.Id,
            Username = owner.Username,
            PasswordHash = "x"
        };
        db.SiteAdmins.Add(ownerAdmin);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.InviteAsync(ownerAdmin.Id, "bob");

        Assert.True(await db.SuperUserGrants.AnyAsync(g => g.UserId == invited.Id));
    }

    [Fact]
    public async Task ExistingOperatorLogin_GrantsMissingSuperUser()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        db.SiteAdmins.Add(new SiteAdmin
        {
            UserId = owner.Id,
            Username = owner.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Operator1!")
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.ElevateAsync(owner.Id, new OperatorLoginRequest(AdminPassword: "Operator1!"));

        Assert.True(await db.SuperUserGrants.AnyAsync(g => g.UserId == owner.Id));
    }

    [Fact]
    public async Task InvitingExistingSuperUser_DoesNotFail()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var invited = AddUser(db, "bob");
        var ownerAdmin = new SiteAdmin
        {
            UserId = owner.Id,
            Username = owner.Username,
            PasswordHash = "x"
        };
        db.SiteAdmins.Add(ownerAdmin);
        db.SuperUserGrants.Add(new SuperUserGrant { UserId = invited.Id });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var dto = await service.InviteAsync(ownerAdmin.Id, "bob");

        Assert.Equal(invited.Id, dto.UserId);
        Assert.Equal(1, await db.SuperUserGrants.CountAsync(g => g.UserId == invited.Id));
    }

    [Fact]
    public async Task InvitingOperator_DoesNotGrantSuperUserWhenDisabled()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var invited = AddUser(db, "bob");
        var ownerAdmin = new SiteAdmin
        {
            UserId = owner.Id,
            Username = owner.Username,
            PasswordHash = "x"
        };
        db.SiteAdmins.Add(ownerAdmin);
        await db.SaveChangesAsync();

        var service = CreateService(db, grantOperatorsSuperUser: false);
        await service.InviteAsync(ownerAdmin.Id, "bob");

        Assert.False(await db.SuperUserGrants.AnyAsync(g => g.UserId == invited.Id));
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

    private static SiteAdminAuthService CreateService(SneklDbContext db, bool grantOperatorsSuperUser = true) =>
        new(db, new IndexConfig(grantOperatorsSuperUser));

    private static SneklDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SneklDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SneklDbContext(options);
    }

    private sealed class IndexConfig(bool grantOperatorsSuperUser) : IConfiguration
    {
        public string? this[string key]
        {
            get => key switch
            {
                "Jwt:Key" => "unit-test-jwt-key-that-is-long-enough",
                "Jwt:Issuer" => "snekl-test",
                "Admin:TokenExpiryHours" => "8",
                "Admin:GrantOperatorsSuperUser" => grantOperatorsSuperUser ? "true" : "false",
                _ => null
            };
            set { }
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => new NoopChangeToken();
        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
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
