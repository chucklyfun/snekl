using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.ForumPages;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class ForumPageLayoutTests
{
    [Fact]
    public void DefaultSeed_HasExpectedViews()
    {
        var root = ForumPageLayout.CreateDefaultHomeSeed();
        Assert.Equal(ForumPageViewTypes.Stack, root.TypeKey);
        Assert.Equal(3, root.Children!.Count);
        Assert.Equal(ForumPageViewTypes.CoolerList, root.Children[0].TypeKey);
        Assert.Equal(ForumPageViewTypes.SubforumGrid, root.Children[1].TypeKey);
        Assert.Equal(ForumPageViewTypes.ForumView, root.Children[2].TypeKey);
        ForumPageLayout.Validate(root);
    }

    [Fact]
    public void DefaultChromeSeed_HasHeaderAndPageBody()
    {
        var root = ForumPageLayout.CreateDefaultChromeSeed();
        Assert.Equal(2, root.Children!.Count);
        Assert.Equal(ForumPageViewTypes.ForumHeader, root.Children[0].TypeKey);
        Assert.Equal(ForumPageViewTypes.PageBody, root.Children[1].TypeKey);
        ForumPageLayout.ValidateChrome(root);
    }

    [Fact]
    public void Compose_InjectsPageIntoPageBody()
    {
        var chrome = ForumPageLayout.CreateDefaultChromeSeed();
        var page = ForumPageLayout.CreateDefaultHomeSeed();
        var composed = ForumPageLayout.Compose(chrome, page);
        Assert.Equal(ForumPageViewTypes.Stack, composed.TypeKey);
        Assert.Equal(ForumPageViewTypes.ForumHeader, composed.Children![0].TypeKey);
        Assert.Equal(ForumPageViewTypes.Stack, composed.Children[1].TypeKey);
        Assert.Equal(ForumPageViewTypes.CoolerList, composed.Children[1].Children![0].TypeKey);
    }

    [Fact]
    public void Validate_RejectsPageBodyOnPage()
    {
        var root = ForumPageLayout.CreateDefaultHomeSeed();
        root.Children!.Add(new ForumPageRegion { TypeKey = ForumPageViewTypes.PageBody });
        Assert.Throws<InvalidOperationException>(() => ForumPageLayout.Validate(root));
    }

    [Fact]
    public void ValidateChrome_RequiresExactlyOnePageBody()
    {
        var root = ForumPageLayout.CreateDefaultChromeSeed();
        root.Children!.RemoveAt(1);
        Assert.Throws<InvalidOperationException>(() => ForumPageLayout.ValidateChrome(root));
    }

    [Fact]
    public void DefaultSeed_WithPin_InsertsWatercoolerView()
    {
        var coolerId = Guid.NewGuid();
        var root = ForumPageLayout.CreateDefaultHomeSeed(coolerId);
        Assert.Equal(4, root.Children!.Count);
        Assert.Equal(ForumPageViewTypes.WatercoolerView, root.Children[1].TypeKey);
        Assert.Equal(coolerId, ForumPageLayout.FindPinnedWatercoolerId(root));
        ForumPageLayout.Validate(root);
    }

    [Fact]
    public void Validate_RejectsWatercoolerViewWithoutId()
    {
        var root = ForumPageLayout.CreateDefaultHomeSeed();
        root.Children!.Add(new ForumPageRegion
        {
            TypeKey = ForumPageViewTypes.WatercoolerView,
            Settings = new Dictionary<string, JsonElement>()
        });
        Assert.Throws<InvalidOperationException>(() => ForumPageLayout.Validate(root));
    }

    [Fact]
    public void SoftGuidance_WarnsWithoutPrimaryView()
    {
        var root = new ForumPageRegion
        {
            TypeKey = ForumPageViewTypes.Stack,
            Children =
            [
                new ForumPageRegion { TypeKey = ForumPageViewTypes.ForumHeader }
            ]
        };
        var warnings = ForumPageLayout.SoftGuidance(root);
        Assert.Contains(warnings, w => w.Contains("forum-view", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EnsureDefaultHome_MigratesPinnedCoolerFeed()
    {
        await using var db = CreateDb();
        var forumId = Guid.NewGuid();
        var coolerId = Guid.NewGuid();
        db.Forums.Add(new Forum
        {
            Id = forumId,
            Name = "Club",
            Slug = "club",
            Description = "",
            CreatedByUserId = Guid.NewGuid()
        });
        db.Watercoolers.Add(new Watercooler
        {
            Id = coolerId,
            Name = "Lounge",
            Slug = "lounge",
            Description = "",
            CreatedByUserId = Guid.NewGuid(),
            ParentForumId = forumId
        });
        db.ForumContentBlocks.Add(new ForumContentBlock
        {
            ForumId = forumId,
            TypeKey = WatercoolerBlockTypes.CoolerFeed,
            WatercoolerId = coolerId,
            Slot = ContentBlockSlots.Main,
            Position = 0
        });
        await db.SaveChangesAsync();

        var service = new ForumPageService(db, new PermissionService(db));
        var page = await service.EnsureDefaultHomeAsync(forumId);

        Assert.True(page.IsDefault);
        Assert.Equal(ForumPageSlugs.Home, page.Slug);
        Assert.Equal(ForumPageViewTypes.Stack, page.Layout.TypeKey);
        var pinned = page.Layout.Children?.FirstOrDefault(c => c.TypeKey == ForumPageViewTypes.WatercoolerView);
        Assert.NotNull(pinned);
        Assert.True(pinned!.Settings!.TryGetValue("watercoolerId", out var el));
        Assert.Equal(coolerId, el.Deserialize<Guid>());
    }

    [Fact]
    public async Task EnsureChrome_StripsHeaderFromExistingHome()
    {
        await using var db = CreateDb();
        var forumId = Guid.NewGuid();
        db.Forums.Add(new Forum
        {
            Id = forumId,
            Name = "Club",
            Slug = "club",
            Description = "",
            CreatedByUserId = Guid.NewGuid()
        });
        var legacyHome = new ForumPageRegion
        {
            Id = Guid.NewGuid(),
            TypeKey = ForumPageViewTypes.Stack,
            Children =
            [
                new ForumPageRegion { TypeKey = ForumPageViewTypes.ForumHeader },
                new ForumPageRegion { TypeKey = ForumPageViewTypes.ForumView }
            ]
        };
        db.ForumPages.Add(new ForumPage
        {
            ForumId = forumId,
            Title = "Home",
            Slug = ForumPageSlugs.Home,
            IsDefault = true,
            LayoutJson = ForumPageLayout.Serialize(legacyHome)
        });
        await db.SaveChangesAsync();

        var service = new ForumPageService(db, new PermissionService(db));
        var chrome = await service.EnsureChromeAsync(forumId);
        Assert.True(chrome.IsChrome);
        Assert.Equal(ForumPageSlugs.Chrome, chrome.Slug);
        Assert.Equal(ForumPageViewTypes.PageBody, chrome.Layout.Children![1].TypeKey);

        var home = await service.EnsureDefaultHomeAsync(forumId);
        Assert.DoesNotContain(home.Layout.Children!, c => c.TypeKey == ForumPageViewTypes.ForumHeader);
        Assert.Contains(home.Layout.Children!, c => c.TypeKey == ForumPageViewTypes.ForumView);
    }

    [Fact]
    public async Task UpdateDefaultHomeLayout_RequiresManageContentBlocks()
    {
        await using var db = CreateDb();
        var owner = new User
        {
            Username = "owner",
            DisplayName = "Owner",
            Email = "owner@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(owner);
        var forumId = Guid.NewGuid();
        db.Forums.Add(new Forum
        {
            Id = forumId,
            Name = "Club",
            Slug = "club",
            Description = "",
            CreatedByUserId = owner.Id,
            OwnerUserId = owner.Id
        });
        await db.SaveChangesAsync();

        var permissions = new PermissionService(db);
        var seed = new RoleSeedService(db);
        await seed.SeedForumRolesAsync(forumId, owner.Id);
        var service = new ForumPageService(db, permissions);

        var stranger = new User
        {
            Username = "stranger",
            DisplayName = "Stranger",
            Email = "s@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(stranger);
        await db.SaveChangesAsync();

        var layout = ForumPageLayout.CreateDefaultHomeSeed();
        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            service.UpdateDefaultHomeLayoutAsync(
                forumId,
                stranger.Id,
                new UpdateForumHomeLayoutRequest(ForumPageService.ToRegionDto(layout))));

        var saved = await service.UpdateDefaultHomeLayoutAsync(
            forumId,
            owner.Id,
            new UpdateForumHomeLayoutRequest(ForumPageService.ToRegionDto(layout)));
        Assert.Equal(ForumPageViewTypes.Stack, saved.Layout.TypeKey);
        Assert.Equal(3, saved.Layout.Children!.Count);
    }

    [Fact]
    public void Validate_RejectsNonPositiveSize()
    {
        var root = ForumPageLayout.CreateDefaultHomeSeed();
        root.Children![0].Settings = new Dictionary<string, JsonElement>
        {
            ["sizeMode"] = JsonSerializer.SerializeToElement("fr"),
            ["size"] = JsonSerializer.SerializeToElement(0)
        };
        Assert.Throws<InvalidOperationException>(() => ForumPageLayout.Validate(root));
    }

    [Fact]
    public void Validate_AcceptsGridWithColumnSpan()
    {
        var root = new ForumPageRegion
        {
            TypeKey = ForumPageViewTypes.Grid,
            Settings = new Dictionary<string, JsonElement>
            {
                ["columns"] = JsonSerializer.SerializeToElement(2),
                ["gap"] = JsonSerializer.SerializeToElement(16),
                ["stackBelow"] = JsonSerializer.SerializeToElement(720)
            },
            Children =
            [
                new ForumPageRegion
                {
                    TypeKey = ForumPageViewTypes.ForumView,
                    Settings = new Dictionary<string, JsonElement>
                    {
                        ["colSpan"] = JsonSerializer.SerializeToElement(2)
                    }
                },
                new ForumPageRegion { TypeKey = ForumPageViewTypes.CoolerList },
                new ForumPageRegion { TypeKey = ForumPageViewTypes.SubforumGrid }
            ]
        };
        ForumPageLayout.Validate(root);
    }

    [Fact]
    public void Validate_RejectsGridColumnCount()
    {
        var root = new ForumPageRegion
        {
            TypeKey = ForumPageViewTypes.Grid,
            Settings = new Dictionary<string, JsonElement>
            {
                ["columns"] = JsonSerializer.SerializeToElement(0)
            },
            Children = [new ForumPageRegion { TypeKey = ForumPageViewTypes.ForumView }]
        };
        Assert.Throws<InvalidOperationException>(() => ForumPageLayout.Validate(root));
    }

    [Fact]
    public void Validate_RejectsBadSizeMode()
    {
        var root = ForumPageLayout.CreateDefaultHomeSeed();
        root.Children![0].Settings = new Dictionary<string, JsonElement>
        {
            ["sizeMode"] = JsonSerializer.SerializeToElement("em")
        };
        Assert.Throws<InvalidOperationException>(() => ForumPageLayout.Validate(root));
    }

    private static SneklDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<SneklDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
