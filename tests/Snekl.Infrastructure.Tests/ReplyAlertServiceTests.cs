using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class ReplyAlertServiceTests
{
    [Fact]
    public async Task DirectReply_AlertsParentAuthor()
    {
        await using var db = CreateDb();
        var (alice, bob, root) = await SeedThreadAsync(db);
        db.PushDevices.Add(new PushDevice
        {
            UserId = alice.Id,
            Platform = "android",
            Token = "tok-alice-1"
        });
        await db.SaveChangesAsync();
        var push = new RecordingPush();
        var service = CreateService(db, push);

        var reply = await AddReplyAsync(db, root, bob, "first");
        await service.RecordReplyAsync(reply);

        var alerts = await service.ListAsync(alice.Id, 20);
        var alert = Assert.Single(alerts);
        Assert.Equal(1, alert.ReplyCount);
        Assert.Equal(root.Id, alert.RootPostId);
        Assert.Equal("Hello", alert.Title);
        Assert.Null(alert.ReadAt);
        Assert.Equal(1, await service.UnreadCountAsync(alice.Id));
        Assert.Empty(await service.ListAsync(bob.Id, 20));
        var sent = Assert.Single(push.Sent);
        Assert.Equal(alert.Id, sent.Push.AlertId);
        Assert.Contains("Bob replied to Hello", sent.Push.Body);
    }

    [Fact]
    public async Task NestedReply_DoesNotAlertThreadAuthorByDefault()
    {
        await using var db = CreateDb();
        var (alice, bob, root) = await SeedThreadAsync(db);
        var carol = AddUser(db, "carol");
        await db.SaveChangesAsync();
        var reply = await AddReplyAsync(db, root, bob, "from bob");
        var nested = await AddReplyAsync(db, reply, carol, "from carol");
        var service = CreateService(db, new RecordingPush());

        await service.RecordReplyAsync(nested);

        Assert.Empty(await service.ListAsync(alice.Id, 20));
        var bobAlerts = await service.ListAsync(bob.Id, 20);
        Assert.Equal(1, Assert.Single(bobAlerts).ReplyCount);
    }

    [Fact]
    public async Task AllActivity_IncludesThreadAuthor_Off_SuppressesDirectReply()
    {
        await using var db = CreateDb();
        var (alice, bob, root) = await SeedThreadAsync(db);
        var service = CreateService(db, new RecordingPush());
        await service.SetPreferenceAsync(alice.Id, root.Id, ReplyAlertModes.All);

        var reply = await AddReplyAsync(db, root, bob, "one");
        var nestedParent = reply;
        var carol = AddUser(db, "carol");
        await db.SaveChangesAsync();
        var nested = await AddReplyAsync(db, nestedParent, carol, "two");
        await service.RecordReplyAsync(nested);

        Assert.Equal(1, Assert.Single(await service.ListAsync(alice.Id, 20)).ReplyCount);
        Assert.Equal(1, Assert.Single(await service.ListAsync(bob.Id, 20)).ReplyCount);

        await service.SetPreferenceAsync(bob.Id, root.Id, ReplyAlertModes.Off);
        await service.RecordReplyAsync(await AddReplyAsync(db, reply, carol, "three"));

        Assert.Equal(1, Assert.Single(await service.ListAsync(bob.Id, 20)).ReplyCount);
        Assert.Equal(2, Assert.Single(await service.ListAsync(alice.Id, 20)).ReplyCount);
    }

    [Fact]
    public async Task Burst_CollapsesUntilRead_ThenStartsANewRow()
    {
        await using var db = CreateDb();
        var (alice, bob, root) = await SeedThreadAsync(db);
        db.PushDevices.Add(new PushDevice
        {
            UserId = alice.Id,
            Platform = "android",
            Token = "device-token-1"
        });
        db.NodeConfig.Add(new NodeConfig { Did = "did:web:alpha", Name = "Alpha" });
        await db.SaveChangesAsync();
        var push = new RecordingPush();
        var service = CreateService(db, push);

        var first = await AddReplyAsync(db, root, bob, "one");
        var second = await AddReplyAsync(db, root, bob, "two");
        await service.RecordReplyAsync(first);
        await service.RecordReplyAsync(second);

        var open = Assert.Single(await service.ListAsync(alice.Id, 20));
        Assert.Equal(2, open.ReplyCount);
        Assert.Equal(second.Id, open.LatestReplyId);
        Assert.Equal(2, push.Sent.Count);
        Assert.Equal(push.Sent[0].Push.AlertId, push.Sent[1].Push.AlertId);
        Assert.Equal("did:web:alpha", push.Sent[1].Push.ServerDid);
        Assert.Contains("2 new replies", push.Sent[1].Push.Body);

        await service.MarkReadAsync(alice.Id, open.Id);
        Assert.Equal(0, await service.UnreadCountAsync(alice.Id));

        var third = await AddReplyAsync(db, root, bob, "three");
        await service.RecordReplyAsync(third);

        var history = await service.ListAsync(alice.Id, 20);
        Assert.Equal(2, history.Count);
        Assert.Equal(1, history[0].ReplyCount);
        Assert.Null(history[0].ReadAt);
        Assert.NotNull(history[1].ReadAt);
        Assert.NotEqual(history[0].Id, history[1].Id);
    }

    [Fact]
    public async Task SelfReply_CreatesNoAlert()
    {
        await using var db = CreateDb();
        var (alice, _, root) = await SeedThreadAsync(db);
        var service = CreateService(db, new RecordingPush());
        var reply = await AddReplyAsync(db, root, alice, "bump");

        await service.RecordReplyAsync(reply);

        Assert.Empty(await service.ListAsync(alice.Id, 20));
    }

    [Fact]
    public async Task NestWatch_NewPostsAlertPostsAndEverything_RepliesAlertEverythingOnly()
    {
        await using var db = CreateDb();
        var (alice, bob, root) = await SeedThreadAsync(db);
        var carol = AddUser(db, "carol");
        await db.SaveChangesAsync();
        var service = CreateService(db, new RecordingPush());
        var forumId = root.ForumId!.Value;

        Assert.Equal(PlaceAlertModes.Off, (await service.GetPlacePreferenceAsync(bob.Id, ReplyAlertKinds.Nest, forumId)).Mode);
        await service.SetPlacePreferenceAsync(bob.Id, ReplyAlertKinds.Nest, forumId, PlaceAlertModes.Posts);
        await service.SetPlacePreferenceAsync(alice.Id, ReplyAlertKinds.Nest, forumId, PlaceAlertModes.All);

        var fresh = new Post
        {
            ForumId = forumId,
            AuthorUserId = carol.Id,
            Title = "Fresh",
            Message = "hello nest"
        };
        db.Posts.Add(fresh);
        await db.SaveChangesAsync();
        await service.RecordReplyAsync(fresh);

        var bobAlert = Assert.Single(await service.ListAsync(bob.Id, 20));
        Assert.Equal(ReplyAlertKinds.Nest, bobAlert.Kind);
        Assert.Equal("General", bobAlert.Title);
        Assert.Equal(1, bobAlert.ReplyCount);
        var aliceAlert = Assert.Single(await service.ListAsync(alice.Id, 20));
        Assert.Equal(1, aliceAlert.ReplyCount);

        var reply = await AddReplyAsync(db, fresh, bob, "more");
        await service.RecordReplyAsync(reply);

        Assert.Equal(1, Assert.Single(await service.ListAsync(bob.Id, 20)).ReplyCount);
        Assert.Equal(2, Assert.Single(await service.ListAsync(alice.Id, 20)).ReplyCount);
        Assert.Equal(ReplyAlertKinds.Thread, Assert.Single(await service.ListAsync(carol.Id, 20)).Kind);
    }

    [Fact]
    public async Task Preference_DefaultsToReplies_AndRoundTrips()
    {
        await using var db = CreateDb();
        var (alice, _, root) = await SeedThreadAsync(db);
        var service = CreateService(db, new RecordingPush());

        var initial = await service.GetPreferenceAsync(alice.Id, root.Id);
        Assert.Equal(ReplyAlertModes.Replies, initial.Mode);

        var saved = await service.SetPreferenceAsync(alice.Id, root.Id, "OFF");
        Assert.Equal(ReplyAlertModes.Off, saved.Mode);
        Assert.Equal(ReplyAlertModes.Off, (await service.GetPreferenceAsync(alice.Id, root.Id)).Mode);
    }

    [Fact]
    public async Task VoteMilestone_NotifiesAuthorAndNotableFollowers_AtOrdersOfMagnitude()
    {
        await using var db = CreateDb();
        var (alice, _, root) = await SeedThreadAsync(db);
        alice.Did = "did:key:alice";
        alice.AuthIssuerUrl = "http://auth.test";
        var carol = AddUser(db, "carol");
        carol.Did = "did:key:carol";
        var dave = AddUser(db, "dave");
        dave.Did = "did:key:dave";
        db.NodeConfig.Add(new NodeConfig { Did = "did:web:alpha", VoteAlertBase = 10 });
        await db.SaveChangesAsync();
        var follows = new FakeFollows
        {
            Watchers =
            [
                new FollowWatcher(carol.Did, FollowAlertModes.Notable),
                new FollowWatcher(dave.Did, FollowAlertModes.Posts)
            ]
        };
        var service = CreateService(db, new RecordingPush(), follows);

        await service.RecordVoteMilestoneAsync(root, 9, 10);
        Assert.Equal(10, Assert.Single(await service.ListAsync(alice.Id, 20)).ReplyCount);
        Assert.Equal(ReplyAlertKinds.Vote, Assert.Single(await service.ListAsync(carol.Id, 20)).Kind);
        Assert.Equal("Hello", Assert.Single(await service.ListAsync(alice.Id, 20)).Title);
        Assert.Empty(await service.ListAsync(dave.Id, 20));

        await service.RecordVoteMilestoneAsync(root, 10, 11);
        Assert.Equal(10, Assert.Single(await service.ListAsync(alice.Id, 20)).ReplyCount);

        await service.RecordVoteMilestoneAsync(root, 99, 100);
        Assert.Equal(100, Assert.Single(await service.ListAsync(alice.Id, 20)).ReplyCount);

        var open = Assert.Single(await service.ListAsync(alice.Id, 20));
        await service.MarkReadAsync(alice.Id, open.Id);
        await service.RecordVoteMilestoneAsync(root, 999, 1000);
        Assert.Equal(2, (await service.ListAsync(alice.Id, 20)).Count);
        Assert.Contains(await service.ListAsync(alice.Id, 20), alert => alert.ReplyCount == 1000 && alert.ReadAt is null);
    }

    [Fact]
    public async Task VoteMilestone_RespectsServerStartingLevel()
    {
        await using var db = CreateDb();
        var (alice, _, root) = await SeedThreadAsync(db);
        db.NodeConfig.Add(new NodeConfig { VoteAlertBase = 100 });
        await db.SaveChangesAsync();
        var service = CreateService(db, new RecordingPush());

        await service.RecordVoteMilestoneAsync(root, 9, 10);
        Assert.Empty(await service.ListAsync(alice.Id, 20));

        await service.RecordVoteMilestoneAsync(root, 99, 100);
        Assert.Equal(100, Assert.Single(await service.ListAsync(alice.Id, 20)).ReplyCount);
    }

    [Fact]
    public async Task FollowedPost_AlertsPostsMode_AndCollapses()
    {
        await using var db = CreateDb();
        var (alice, _, root) = await SeedThreadAsync(db);
        alice.Did = "did:key:alice";
        alice.AuthIssuerUrl = "http://auth.test";
        var carol = AddUser(db, "carol");
        carol.Did = "did:key:carol";
        var dave = AddUser(db, "dave");
        dave.Did = "did:key:dave";
        await db.SaveChangesAsync();
        var follows = new FakeFollows
        {
            Watchers =
            [
                new FollowWatcher(carol.Did, FollowAlertModes.Posts),
                new FollowWatcher(dave.Did, FollowAlertModes.Notable)
            ]
        };
        var service = CreateService(db, new RecordingPush(), follows);

        await service.RecordReplyAsync(root);
        var first = Assert.Single(await service.ListAsync(carol.Id, 20));
        Assert.Equal(ReplyAlertKinds.Follow, first.Kind);
        Assert.Equal("Alice", first.Title);
        Assert.Equal(root.Id, first.RootPostId);
        Assert.Empty(await service.ListAsync(dave.Id, 20));
        Assert.Empty(await service.ListAsync(alice.Id, 20));

        var second = new Post
        {
            ForumId = root.ForumId,
            AuthorUserId = alice.Id,
            Title = "Again",
            Message = "more"
        };
        db.Posts.Add(second);
        await db.SaveChangesAsync();
        await service.RecordReplyAsync(second);

        var burst = Assert.Single(await service.ListAsync(carol.Id, 20));
        Assert.Equal(2, burst.ReplyCount);
        Assert.Null(burst.RootPostId);
    }

    [Fact]
    public void VoteLevels_AreOrdersOfMagnitudeFromTheBase()
    {
        Assert.Equal(10, VoteAlertLevels.Crossed(9, 10, 10));
        Assert.Null(VoteAlertLevels.Crossed(10, 11, 10));
        Assert.Equal(1000, VoteAlertLevels.Crossed(99, 1000, 10));
        Assert.Null(VoteAlertLevels.Crossed(9, 10, 100));
        Assert.Equal(VoteAlertLevels.DefaultBase, VoteAlertLevels.NormalizeBase(0));
    }

    [Fact]
    public void FcmLegacyResponse_DetectsUnregisteredTokens()
    {
        Assert.True(FcmLegacyResponse.IsUnregistered("{\"results\":[{\"error\":\"NotRegistered\"}]}"));
        Assert.True(FcmLegacyResponse.IsUnregistered("InvalidRegistration"));
        Assert.False(FcmLegacyResponse.IsUnregistered("{\"success\":1}"));
    }

    private static ReplyAlertService CreateService(SneklDbContext db, RecordingPush push, FakeFollows? follows = null) =>
        new(db, new PermissionService(db), push, NullLogger<ReplyAlertService>.Instance, follows ?? new FakeFollows());

    private static async Task<(User Alice, User Bob, Post Root)> SeedThreadAsync(SneklDbContext db)
    {
        var alice = AddUser(db, "alice");
        var bob = AddUser(db, "bob");
        var forum = new Forum
        {
            Name = "General",
            Slug = "general-" + Guid.NewGuid().ToString("N")[..8],
            CreatedByUserId = alice.Id,
            OwnerUserId = alice.Id,
            ApprovalStatus = ForumApprovalStatuses.Approved
        };
        db.Forums.Add(forum);
        await db.SaveChangesAsync();
        var roles = new RoleSeedService(db);
        await roles.EnsureServerRolesAsync();
        await roles.SeedForumRolesAsync(forum.Id, alice.Id);

        var root = new Post
        {
            ForumId = forum.Id,
            AuthorUserId = alice.Id,
            Title = "Hello",
            Message = "hi"
        };
        db.Posts.Add(root);
        await db.SaveChangesAsync();
        return (alice, bob, root);
    }

    private static User AddUser(SneklDbContext db, string name)
    {
        var user = new User
        {
            Username = name,
            DisplayName = char.ToUpper(name[0]) + name[1..],
            Email = $"{name}-{Guid.NewGuid():N}@example.com"
        };
        db.Users.Add(user);
        return user;
    }

    private static async Task<Post> AddReplyAsync(SneklDbContext db, Post parent, User author, string message)
    {
        var reply = new Post
        {
            ForumId = parent.ForumId,
            ParentPostId = parent.Id,
            AuthorUserId = author.Id,
            Message = message
        };
        db.Posts.Add(reply);
        await db.SaveChangesAsync();
        return reply;
    }

    private static SneklDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<SneklDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeFollows : IFollowGraphClient
    {
        public List<FollowWatcher> Watchers { get; init; } = [];

        public Task<bool> FollowsAsync(string? issuerUrl, string followerDid, string targetDid, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<FollowWatcher>> ListWatchersAsync(string? issuerUrl, string targetDid, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FollowWatcher>>(Watchers);
    }

    private sealed class RecordingPush : IReplyAlertPushSender
    {
        public List<(string Token, ReplyAlertPush Push)> Sent { get; } = [];

        public Task<IReadOnlyList<string>> SendAsync(
            IReadOnlyList<PushDevice> devices,
            ReplyAlertPush push,
            CancellationToken ct = default)
        {
            foreach (var device in devices)
                Sent.Add((device.Token, push));
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }
}
