using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;
using Snekl.Redis;

namespace Snekl.Infrastructure.Tests;

public class ExtensionInstallServiceTests
{
    [Fact]
    public async Task Create_ReusesActiveInstallForSameClient()
    {
        await using var db = CreateDb();
        var user = AddSuper(db, "alice");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var first = await service.CreateAsync(user.Id, Request("snekl-faker"));
        var second = await service.CreateAsync(user.Id, Request("snekl-faker", "Snekl Faker"));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.ExtensionInstalls.CountAsync(i => i.RevokedAt == null));
        Assert.Equal("Snekl Faker", second.AppName);
    }

    [Fact]
    public async Task ListActive_ReturnsOneRowPerClient()
    {
        await using var db = CreateDb();
        var user = AddSuper(db, "alice");
        await db.SaveChangesAsync();
        db.ExtensionInstalls.AddRange(
            Install(user.Id, "snekl-faker", DateTime.UtcNow.AddMinutes(-5)),
            Install(user.Id, "snekl-faker", DateTime.UtcNow.AddMinutes(-1)),
            Install(user.Id, "other-app", DateTime.UtcNow));
        await db.SaveChangesAsync();

        var listed = await CreateService(db).ListActiveAsync();

        Assert.Equal(2, listed.Count);
        Assert.Contains(listed, i => i.ClientId == "snekl-faker");
        Assert.Contains(listed, i => i.ClientId == "other-app");
        Assert.Equal(1, listed.Count(i => i.ClientId == "snekl-faker"));
    }

    [Fact]
    public async Task GetOptions_FindsInstallCreatedByAnotherAdmin()
    {
        await using var db = CreateDb();
        var first = AddSuper(db, "alice");
        var second = AddSuper(db, "bob");
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var install = await service.CreateAsync(first.Id, Request("snekl-faker"));

        var options = await service.GetOptionsAsync(second.Id, "snekl-faker");

        Assert.NotNull(options.ExistingInstall);
        Assert.Equal(install.Id, options.ExistingInstall!.Id);
    }

    private static CreateExtensionInstallRequest Request(string clientId, string? name = null) =>
        new(
            clientId,
            name ?? clientId,
            "http://localhost:5300/",
            null,
            "server",
            null,
            ["ViewServer", "CreateForum"],
            ["SeeForum", "ViewForum", "CreateThread", "Reply"]);

    private static ExtensionInstall Install(Guid userId, string clientId, DateTime created) =>
        new()
        {
            ClientId = clientId,
            AppName = clientId,
            InstalledByUserId = userId,
            InstalledByDid = "",
            ResourceType = "server",
            CreatedAt = created
        };

    private static User AddSuper(SneklDbContext db, string name)
    {
        var user = new User
        {
            Username = name,
            DisplayName = name,
            Email = $"{name}@example.com",
            PasswordHash = "x",
            Did = $"did:key:{name}"
        };
        db.Users.Add(user);
        db.SuperUserGrants.Add(new SuperUserGrant { UserId = user.Id });
        return user;
    }

    private static IExtensionInstallService CreateService(SneklDbContext db) =>
        new ExtensionInstallService(
            db,
            new PermissionService(db),
            Options.Create(new RateLimitOptions { Enabled = false }),
            new ConfigurationBuilder().Build());

    private static SneklDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<SneklDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
