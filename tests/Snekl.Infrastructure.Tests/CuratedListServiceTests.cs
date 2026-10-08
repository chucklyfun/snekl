using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class CuratedListServiceTests
{
    [Fact]
    public async Task Create_RejectsUnknownBehavior()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        await db.SaveChangesAsync();

        var lists = new CuratedListService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lists.CreateAsync(owner.Id, new CreateCuratedListRequest(
                "Bad", "", "not-a-behavior", "★", null, true)));
    }

    [Fact]
    public async Task PublicList_VisibleToOthers_PrivateOnlyToOwner()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var other = AddUser(db, "other");
        await db.SaveChangesAsync();
        var lists = new CuratedListService(db);

        var pub = await lists.CreateAsync(owner.Id, new CreateCuratedListRequest(
            "Verified", "celebs", "Verify", "✅", null, true));
        Assert.True(pub.IsViewerSubscribed);
        Assert.Equal(1, pub.SubscriberCount);
        Assert.Equal("owner", pub.OwnerUsername);

        var priv = await lists.CreateAsync(owner.Id, new CreateCuratedListRequest(
            "Secret", "mine", "Highlight", "★", null, false));

        var asOwner = await lists.GetAsync(pub.Id, owner.Id);
        Assert.True(asOwner!.IsViewerSubscribed);

        var asOther = await lists.ListAsync(other.Id);
        Assert.Contains(asOther, l => l.Id == pub.Id);
        Assert.DoesNotContain(asOther, l => l.Id == priv.Id);

        Assert.Null(await lists.GetAsync(priv.Id, other.Id));
        Assert.NotNull(await lists.GetAsync(priv.Id, owner.Id));
    }

    [Fact]
    public async Task AddEntry_OwnerOnly_RequiresLabel_RejectsDuplicate()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var other = AddUser(db, "other");
        var target = AddUser(db, "target");
        await db.SaveChangesAsync();
        var lists = new CuratedListService(db);
        var list = await lists.CreateAsync(owner.Id, new CreateCuratedListRequest(
            "Warn list", "", "Warn", "⚠", null, true));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lists.AddEntryAsync(list.Id, other.Id, Entry(target.Id, "Nope")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lists.AddEntryAsync(list.Id, owner.Id, Entry(target.Id, "  ")));

        var added = await lists.AddEntryAsync(list.Id, owner.Id, Entry(target.Id, "Caution"));
        Assert.Equal("Caution", added.Label);
        Assert.Equal((int)AnnotationSeverity.Warn, added.Severity);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lists.AddEntryAsync(list.Id, owner.Id, Entry(target.Id, "Again")));
    }

    [Fact]
    public async Task UpdateAndDelete_OwnerOnly_RemovesEntries()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var other = AddUser(db, "other");
        var target = AddUser(db, "target");
        await db.SaveChangesAsync();
        var lists = new CuratedListService(db);
        var list = await lists.CreateAsync(owner.Id, new CreateCuratedListRequest(
            "Warn list", "old", "Warn", "⚠", null, true));
        var added = await lists.AddEntryAsync(list.Id, owner.Id, Entry(target.Id, "Caution"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lists.UpdateAsync(list.Id, other.Id, new UpdateCuratedListRequest(
                "Hijack", "", "Highlight", "★", null, true)));

        var updated = await lists.UpdateAsync(list.Id, owner.Id, new UpdateCuratedListRequest(
            "Trusted", "people we know", "Verify", "✅", null, true));
        Assert.Equal("Trusted", updated.Name);
        Assert.Equal("Verify", updated.DefaultBehavior);

        var renamed = await lists.UpdateEntryAsync(list.Id, added.Id, owner.Id, new UpdateListEntryRequest(
            "VIP", "updated tip", "✅", null));
        Assert.Equal("VIP", renamed.Label);

        var entries = await lists.ListEntriesAsync(list.Id, owner.Id);
        Assert.Equal("target", Assert.Single(entries.Items).TargetUsername);

        await lists.DeleteEntryAsync(list.Id, added.Id, owner.Id);
        Assert.Empty((await lists.ListEntriesAsync(list.Id, owner.Id)).Items);

        await lists.DeleteAsync(list.Id, owner.Id);
        Assert.Null(await lists.GetAsync(list.Id, owner.Id));
    }

    [Fact]
    public async Task TransferOwnership_MovesOwner_AndRequiresALocalAccount()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var next = AddUser(db, "next");
        var stranger = AddUser(db, "stranger");
        await db.SaveChangesAsync();
        var lists = new CuratedListService(db);
        var list = await lists.CreateAsync(owner.Id, new CreateCuratedListRequest(
            "Crew", "people", "Highlight", "★", null, true));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lists.TransferOwnershipAsync(list.Id, stranger.Id, "next"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lists.TransferOwnershipAsync(list.Id, owner.Id, "owner"));
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lists.TransferOwnershipAsync(list.Id, owner.Id, "@ghost"));
        Assert.Contains("refresh their login from the auth server", missing.Message);

        var transferred = await lists.TransferOwnershipAsync(list.Id, owner.Id, "@Next");
        Assert.False(transferred.IsOwner);
        Assert.Equal("next", transferred.OwnerUsername);
        Assert.True(transferred.IsViewerSubscribed);

        var asNext = await lists.GetAsync(list.Id, next.Id);
        Assert.True(asNext!.IsOwner);
        Assert.True(asNext.IsViewerSubscribed);
        Assert.Equal(2, asNext.SubscriberCount);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lists.UpdateAsync(list.Id, owner.Id, new UpdateCuratedListRequest(
                "Hijack", "", "Highlight", "★", null, true)));
    }

    [Fact]
    public async Task Subscribe_StoresAppliedBehavior_HideIsRestrictSeverity_NotFeedFilter()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var viewer = AddUser(db, "viewer");
        var target = AddUser(db, "target");
        await db.SaveChangesAsync();
        var lists = new CuratedListService(db);
        var list = await lists.CreateAsync(owner.Id, new CreateCuratedListRequest(
            "Mute list", "", "Mute", "🔇", null, true));
        await lists.AddEntryAsync(list.Id, owner.Id, Entry(target.Id, "Muted person"));

        await lists.SubscribeAsync(list.Id, viewer.Id, new SubscribeListRequest("Hide"));
        var sub = await db.ListSubscriptions.SingleAsync(s => s.ListId == list.Id && s.UserId == viewer.Id);
        Assert.Equal(ListBehavior.Hide, sub.AppliedBehavior);

        await lists.SubscribeAsync(list.Id, viewer.Id, new SubscribeListRequest("Highlight"));
        Assert.Equal(ListBehavior.Highlight, (await db.ListSubscriptions.SingleAsync(s => s.ListId == list.Id && s.UserId == viewer.Id)).AppliedBehavior);

        await lists.UnsubscribeAsync(list.Id, viewer.Id);
        await lists.UnsubscribeAsync(list.Id, viewer.Id);
        Assert.Empty(await db.ListSubscriptions.Where(s => s.UserId == viewer.Id).ToListAsync());

        var presentation = CreatePresentation(db);
        await lists.SubscribeAsync(list.Id, viewer.Id, new SubscribeListRequest("Mute"));
        var seen = await presentation.GetPresentationAsync(target.Id, viewer.Id);
        var note = Assert.Single(seen.Annotations);
        Assert.Equal((int)AnnotationSeverity.Restrict, note.Severity);
        Assert.Equal("Muted person", note.Label);
    }

    [Fact]
    public async Task ListEntries_Pages_AndFiltersByUsernameOrLabel()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var alice = AddUser(db, "alice");
        var bob = AddUser(db, "bob");
        var cara = AddUser(db, "cara");
        await db.SaveChangesAsync();
        var lists = new CuratedListService(db);
        var list = await lists.CreateAsync(owner.Id, new CreateCuratedListRequest(
            "Crew", "", "Highlight", "★", null, true));
        await lists.AddEntryAsync(list.Id, owner.Id, Entry(alice.Id, "VIP"));
        await lists.AddEntryAsync(list.Id, owner.Id, Entry(bob.Id, "Warning"));
        await lists.AddEntryAsync(list.Id, owner.Id, Entry(cara.Id, "VIP"));

        var page1 = await lists.ListEntriesAsync(list.Id, owner.Id, page: 1, pageSize: 2);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal("alice", page1.Items[0].TargetUsername);

        var page2 = await lists.ListEntriesAsync(list.Id, owner.Id, page: 2, pageSize: 2);
        Assert.Single(page2.Items);
        Assert.Equal("cara", page2.Items[0].TargetUsername);

        var filtered = await lists.ListEntriesAsync(list.Id, owner.Id, q: "@bob");
        Assert.Equal("bob", Assert.Single(filtered.Items).TargetUsername);

        var byLabel = await lists.ListEntriesAsync(list.Id, owner.Id, q: "warn");
        Assert.Equal("bob", Assert.Single(byLabel.Items).TargetUsername);
    }

    [Fact]
    public async Task Annotations_OnlyForSubscribers_CacheMayStayStaleUntilNewLookup()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var subscriber = AddUser(db, "sub");
        var stranger = AddUser(db, "str");
        var target = AddUser(db, "target");
        await db.SaveChangesAsync();
        var lists = new CuratedListService(db);
        var list = await lists.CreateAsync(owner.Id, new CreateCuratedListRequest(
            "Stars", "", "Highlight", "★", null, true));
        await lists.AddEntryAsync(list.Id, owner.Id, Entry(target.Id, "VIP", "A known face"));
        await lists.SubscribeAsync(list.Id, subscriber.Id, new SubscribeListRequest("Highlight"));

        var cache = MemoryCache();
        var presentation = new UserPresentationService(db, cache);

        var asStranger = await presentation.GetPresentationAsync(target.Id, stranger.Id);
        Assert.Empty(asStranger.Annotations);

        var asSub = await presentation.GetPresentationAsync(target.Id, subscriber.Id);
        var annotation = Assert.Single(asSub.Annotations);
        Assert.Equal("VIP", annotation.Label);
        Assert.Equal("A known face", annotation.TooltipText);
        Assert.Equal("★", annotation.FlairEmoji);
        Assert.Equal((int)AnnotationSeverity.Highlight, annotation.Severity);

        await lists.UnsubscribeAsync(list.Id, subscriber.Id);
        var stillCached = await presentation.GetPresentationAsync(target.Id, subscriber.Id);
        Assert.Single(stillCached.Annotations);

        var fresh = new UserPresentationService(db, MemoryCache());
        var after = await fresh.GetPresentationAsync(target.Id, subscriber.Id);
        Assert.Empty(after.Annotations);
    }

    [Fact]
    public async Task SubscriptionPriority_OrdersAnnotations_IgnoreDropsEntryForViewer()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var target = AddUser(db, "target");
        await db.SaveChangesAsync();
        var lists = new CuratedListService(db);
        var first = await lists.CreateAsync(owner.Id, new CreateCuratedListRequest(
            "Low", "", "Highlight", "1️⃣", null, true));
        await lists.AddEntryAsync(first.Id, owner.Id, Entry(target.Id, "From first"));
        var second = await lists.CreateAsync(owner.Id, new CreateCuratedListRequest(
            "High", "", "Warn", "2️⃣", null, true));
        var secondEntry = await lists.AddEntryAsync(second.Id, owner.Id, Entry(target.Id, "From second"));

        var before = await CreatePresentation(db).GetPresentationAsync(target.Id, owner.Id);
        Assert.Equal("From first", before.Annotations[0].Label);
        Assert.Equal("From second", before.Annotations[1].Label);

        await lists.ReorderSubscriptionsAsync(owner.Id, [second.Id, first.Id]);
        var reordered = await CreatePresentation(db).GetPresentationAsync(target.Id, owner.Id);
        Assert.Equal("From second", reordered.Annotations[0].Label);
        Assert.Equal("#f59e0b", reordered.EffectiveNameColor);

        await lists.IgnoreEntryAsync(second.Id, secondEntry.Id, owner.Id);
        var ignored = await lists.ListEntriesAsync(second.Id, owner.Id);
        Assert.True(Assert.Single(ignored.Items).IsIgnoredByViewer);

        var afterIgnore = await CreatePresentation(db).GetPresentationAsync(target.Id, owner.Id);
        Assert.Equal("From first", Assert.Single(afterIgnore.Annotations).Label);

        await lists.UnignoreEntryAsync(second.Id, secondEntry.Id, owner.Id);
        var restored = await CreatePresentation(db).GetPresentationAsync(target.Id, owner.Id);
        Assert.Equal(2, restored.Annotations.Count);

        var hits = await lists.FindEntriesForUserAsync("target", owner.Id);
        Assert.Equal(["From second", "From first"], hits.Select(h => h.Label).ToArray());
        Assert.True(hits[0].AppliesOnPosts);
        Assert.True(hits[1].AppliesOnPosts);

        var other = AddUser(db, "other");
        await db.SaveChangesAsync();
        var asOther = await lists.FindEntriesForUserAsync("@target", other.Id);
        Assert.Equal(2, asOther.Count);
        Assert.All(asOther, h => Assert.False(h.AppliesOnPosts));
    }

    private static AddListEntryRequest Entry(Guid targetId, string label, string tooltip = "tip") =>
        new(targetId, label, tooltip, null, null, null, null, null);

    private static User AddUser(SneklDbContext db, string name)
    {
        var user = new User
        {
            Username = name,
            DisplayName = name,
            Email = $"{name}@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        return user;
    }

    private static UserPresentationService CreatePresentation(SneklDbContext db) =>
        new(db, MemoryCache());

    private static IDistributedCache MemoryCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private static SneklDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<SneklDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
