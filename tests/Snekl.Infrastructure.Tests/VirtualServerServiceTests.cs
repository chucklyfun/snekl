using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class VirtualServerServiceTests
{
    [Fact]
    public async Task Spawn_copies_template_and_isolates_the_owner()
    {
        var current = new CurrentServer();
        await using var db = CreateDb(current);
        var templatePath = Path.Combine(Path.GetTempPath(), $"snekl-template-{Guid.NewGuid():N}.json");
        var store = new ServerTemplateStore(templatePath);
        store.Write(new ServerTemplateDocument
        {
            AuthIssuers =
            [
                new ServerTemplateIssuer
                {
                    Name = "Auth",
                    IssuerUrl = "http://localhost:5100",
                    JwksUrl = "http://localhost:5100/.well-known/jwks.json"
                }
            ],
            Hub = new ServerTemplateHub { Name = "Hub", BaseUrl = "http://localhost:5200" },
            Roles =
            [
                new ServerTemplateRole
                {
                    Name = "@everyone",
                    IsEveryone = true,
                    Position = 0
                }
            ]
        });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Node:BaseUrl"] = "http://localhost:5001" })
            .Build();
        var service = new VirtualServerService(db, store, config);

        var first = await service.SpawnAsync(new SpawnServerRequest("Alpha", "Ada", "did:key:zAda"));
        var second = await service.SpawnAsync(new SpawnServerRequest("Beta", "Ada", "did:key:zAda"));

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.Did, second.Did);
        Assert.Equal("ada", first.OwnerUsername);
        Assert.Equal("http://localhost:5001", first.BaseUrl);
        Assert.Null(first.DisabledAt);

        current.Use(first.Id);
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(1, await db.TrustedAuthIssuers.CountAsync());
        Assert.Equal(1, await db.ServerRoles.CountAsync(r => r.IsEveryone));
        Assert.Equal("http://localhost:5200", await db.HubInvites.Select(i => i.HubBaseUrl).SingleAsync());

        current.Use(second.Id);
        Assert.Equal("Beta", (await db.NodeConfig.SingleAsync(n => n.Id == second.Id)).Name);
        Assert.Equal(1, await db.Users.CountAsync());

        File.Delete(templatePath);
    }

    [Fact]
    public async Task Purge_requires_disable_and_removes_the_server()
    {
        await using var db = CreateDb(new CurrentServer());
        var templatePath = Path.Combine(Path.GetTempPath(), $"snekl-template-{Guid.NewGuid():N}.json");
        var service = new VirtualServerService(db, new ServerTemplateStore(templatePath), new ConfigurationBuilder().Build());
        var spawned = await service.SpawnAsync(new SpawnServerRequest("Gamma", "Bea", "did:key:zBea"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PurgeAsync(spawned.Id));
        await service.DisableAsync(spawned.Id);
        var disabled = await db.NodeConfig.SingleAsync(n => n.Id == spawned.Id);
        Assert.NotNull(disabled.DisabledAt);

        await service.EnableAsync(spawned.Id);
        Assert.Null((await db.NodeConfig.SingleAsync(n => n.Id == spawned.Id)).DisabledAt);

        await service.DisableAsync(spawned.Id);
        await service.PurgeAsync(spawned.Id);
        Assert.Empty(await db.NodeConfig.ToListAsync());
        Assert.Empty(await db.Users.IgnoreQueryFilters().ToListAsync());

        File.Delete(templatePath);
    }

    private static SneklDbContext CreateDb(ICurrentServer current)
    {
        var options = new DbContextOptionsBuilder<SneklDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new SneklDbContext(options, current);
    }
}
