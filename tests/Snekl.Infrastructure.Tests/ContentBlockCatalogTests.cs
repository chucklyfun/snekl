using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class ContentBlockCatalogTests
{
    [Fact]
    public async Task EnsureSeeded_InsertsTypesAndCustomPermissions()
    {
        await using var db = new SneklDbContext(
            new DbContextOptionsBuilder<SneklDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var catalog = new ContentBlockCatalogService(db);
        await catalog.EnsureSeededAsync();
        await catalog.EnsureSeededAsync();

        var types = await catalog.ListTypesAsync();
        Assert.Contains(types, t => t.Key == "calendar");
        Assert.Contains(types, t => t.Key == "ads");
        Assert.Contains(types, t => t.Key == "links");
        Assert.Contains(types, t => t.Key == "extension");
        Assert.Contains(types, t => t.Key == "cooler-list");
        Assert.Contains(types, t => t.Key == "cooler-feed");
        Assert.Equal(6, types.Count);

        var forumNames = await catalog.NamesForScopeAsync(ContentBlockScopes.Forum);
        Assert.Contains("ManageCalendar", forumNames);
        Assert.Contains("ManageAds", forumNames);
        Assert.Contains("ManageLinks", forumNames);

        var serverNames = await catalog.NamesForScopeAsync(ContentBlockScopes.Server);
        Assert.Contains("ManageAds", serverNames);
        Assert.Equal(6, await db.ContentBlockTypes.CountAsync());
    }
}
