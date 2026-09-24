using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class DirectConversationServiceTests
{
    [Fact]
    public async Task SeededEveryoneAndMember_DoNotGrantRequestDirectMessage()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var forum = AddForum(db, owner);
        await db.SaveChangesAsync();
        await new RoleSeedService(db).SeedForumRolesAsync(forum.Id, owner.Id);

        var everyone = await db.ForumRoles.SingleAsync(r => r.ForumId == forum.Id && r.IsEveryone);
        var member = await db.ForumRoles.SingleAsync(r => r.ForumId == forum.Id && r.Name == "Member");
        var moderator = await db.ForumRoles.SingleAsync(r => r.ForumId == forum.Id && r.Name == "Moderator");

        Assert.Equal(GrantMode.None, PermissionModeMap.Get(everyone.Modes, ForumPermissions.RequestDirectMessage));
        Assert.Equal(GrantMode.None, PermissionModeMap.Get(member.Modes, ForumPermissions.RequestDirectMessage));
        Assert.Equal(GrantMode.Direct, PermissionModeMap.Get(moderator.Modes, ForumPermissions.RequestDirectMessage));
        Assert.Equal(
            GrantMode.Direct,
            PermissionModeMap.Get(
                PermissionModeMap.FromForumFlags(ForumPermissions.ForumAdministrator),
                ForumPermissions.RequestDirectMessage));
    }

    [Fact]
    public async Task CreateRequest_DeniedWithoutPermission()
    {
        await using var db = CreateDb();
        var (alice, bob, forum) = await SeedOpenForumAsync(db);
        var service = CreateService(db);

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            service.CreateRequestAsync(alice.Id, new CreateDirectConversationRequestDto("bob", forum.Id)));
    }

    [Fact]
    public async Task CreateRequest_AllowedWhenRoleGrantsPermission()
    {
        await using var db = CreateDb();
        var (alice, bob, forum) = await SeedOpenForumAsync(db);
        GrantRequestPerm(db, forum.Id, alice.Id);
        await db.SaveChangesAsync();

        var created = await CreateService(db).CreateRequestAsync(
            alice.Id,
            new CreateDirectConversationRequestDto("bob", forum.Id, Message: "Follow me back?"));

        Assert.Equal("pending", created.Status);
        Assert.Equal("alice", created.Requester.Username);
        Assert.Equal("bob", created.Target.Username);
        Assert.Equal("Follow me back?", created.Message);
        Assert.Equal(forum.Id, created.Forum.Id);
    }

    [Fact]
    public async Task CreateRequest_RequiresExplicitForum()
    {
        await using var db = CreateDb();
        var (alice, bob, forum) = await SeedOpenForumAsync(db);
        GrantRequestPerm(db, forum.Id, alice.Id);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(db).CreateRequestAsync(alice.Id, new CreateDirectConversationRequestDto("bob")));
        Assert.Contains("Choose the forum", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Accept_OpensConversationForMessaging()
    {
        await using var db = CreateDb();
        var (alice, bob, forum) = await SeedOpenForumAsync(db);
        GrantRequestPerm(db, forum.Id, alice.Id);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var request = await service.CreateRequestAsync(
            alice.Id,
            new CreateDirectConversationRequestDto("bob", forum.Id, Message: "Hi"));

        var accepted = await service.AcceptAsync(bob.Id, request.Id);
        Assert.Equal("accepted", accepted.Status);
        Assert.NotNull(accepted.ConversationId);

        var conversation = await service.GetConversationAsync(bob.Id, accepted.ConversationId!.Value);
        Assert.Equal("alice", conversation.Peer.Username);
        Assert.Single(conversation.Messages);
        Assert.Equal("Hi", conversation.Messages[0].Body);

        var reply = await service.SendMessageAsync(bob.Id, conversation.Id, "Welcome");
        Assert.Equal("Welcome", reply.Body);
        Assert.Equal("bob", reply.Author.Username);
    }

    [Fact]
    public async Task SendMessage_BeforeAccept_Fails()
    {
        await using var db = CreateDb();
        var (alice, bob, forum) = await SeedOpenForumAsync(db);
        GrantRequestPerm(db, forum.Id, alice.Id);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        await service.CreateRequestAsync(alice.Id, new CreateDirectConversationRequestDto("bob", forum.Id));

        var inbox = await service.GetInboxAsync(bob.Id);
        Assert.Single(inbox.IncomingRequests);
        Assert.Empty(inbox.Conversations);
    }

    [Fact]
    public async Task Eligibility_ReportsMissingPermission()
    {
        await using var db = CreateDb();
        var (alice, bob, _) = await SeedOpenForumAsync(db);
        var eligibility = await CreateService(db).GetEligibilityAsync(alice.Id, "bob");
        Assert.False(eligibility.CanRequest);
        Assert.Contains("permission", eligibility.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(eligibility.EligibleForums);
    }

    [Fact]
    public async Task CreateRequest_RejectedWhenTargetRejectsForum()
    {
        await using var db = CreateDb();
        var (alice, bob, forum) = await SeedOpenForumAsync(db);
        GrantRequestPerm(db, forum.Id, alice.Id);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        await service.SetForumPreferenceAsync(bob.Id, forum.Id, DirectMessageForumPreferenceModes.Reject);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateRequestAsync(alice.Id, new CreateDirectConversationRequestDto("bob", forum.Id)));
        Assert.Contains("not accepting", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateRequest_IgnoredWhenTargetIgnoresForum()
    {
        await using var db = CreateDb();
        var (alice, bob, forum) = await SeedOpenForumAsync(db);
        GrantRequestPerm(db, forum.Id, alice.Id);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        await service.SetForumPreferenceAsync(bob.Id, forum.Id, DirectMessageForumPreferenceModes.Ignore);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateRequestAsync(alice.Id, new CreateDirectConversationRequestDto("bob", forum.Id)));
        Assert.Contains("ignoring", ex.Message, StringComparison.OrdinalIgnoreCase);

        var eligibility = await service.GetEligibilityAsync(alice.Id, "bob");
        Assert.False(eligibility.CanRequest);
        Assert.Empty(eligibility.EligibleForums);
    }

    [Fact]
    public async Task SetForumPreference_IgnoreHidesPendingIncoming()
    {
        await using var db = CreateDb();
        var (alice, bob, forum) = await SeedOpenForumAsync(db);
        GrantRequestPerm(db, forum.Id, alice.Id);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        await service.CreateRequestAsync(alice.Id, new CreateDirectConversationRequestDto("bob", forum.Id));
        Assert.Single((await service.GetInboxAsync(bob.Id)).IncomingRequests);

        await service.SetForumPreferenceAsync(bob.Id, forum.Id, DirectMessageForumPreferenceModes.Ignore);

        Assert.Empty((await service.GetInboxAsync(bob.Id)).IncomingRequests);
        Assert.Empty((await service.GetInboxAsync(alice.Id)).OutgoingRequests);
        var stored = await db.DirectConversationRequests.SingleAsync();
        Assert.Equal(DirectConversationRequestStatuses.Ignored, stored.Status);
    }

    [Fact]
    public async Task SetForumPreference_RejectDeclinesPendingIncoming()
    {
        await using var db = CreateDb();
        var (alice, bob, forum) = await SeedOpenForumAsync(db);
        GrantRequestPerm(db, forum.Id, alice.Id);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        await service.CreateRequestAsync(alice.Id, new CreateDirectConversationRequestDto("bob", forum.Id));

        await service.SetForumPreferenceAsync(bob.Id, forum.Id, DirectMessageForumPreferenceModes.Reject);

        Assert.Empty((await service.GetInboxAsync(bob.Id)).IncomingRequests);
        var stored = await db.DirectConversationRequests.SingleAsync();
        Assert.Equal(DirectConversationRequestStatuses.Declined, stored.Status);
    }

    private static DirectConversationService CreateService(SneklDbContext db) =>
        new(db, new PermissionService(db), new ForumModerationService(db));

    private static async Task<(User Alice, User Bob, Forum Forum)> SeedOpenForumAsync(SneklDbContext db)
    {
        var alice = AddUser(db, "alice");
        var bob = AddUser(db, "bob");
        db.ServerRoles.Add(new ServerRole
        {
            Name = "@everyone",
            IsEveryone = true,
            Modes = PermissionModeMap.FromServerFlags(ServerPermissions.ViewServer)
        });
        var forum = AddForum(db, alice);
        db.ForumRoles.Add(new ForumRole
        {
            ForumId = forum.Id,
            Name = "@everyone",
            IsEveryone = true,
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum)
        });
        await db.SaveChangesAsync();
        return (alice, bob, forum);
    }

    private static void GrantRequestPerm(SneklDbContext db, Guid forumId, Guid userId)
    {
        var role = new ForumRole
        {
            ForumId = forumId,
            Name = "Trusted",
            Position = 2,
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum |
                ForumPermissions.RequestDirectMessage)
        };
        db.ForumRoles.Add(role);
        db.SaveChanges();
        db.ForumRoleAssignments.Add(new ForumRoleAssignment
        {
            ForumId = forumId,
            UserId = userId,
            RoleId = role.Id
        });
    }

    private static User AddUser(SneklDbContext db, string name)
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
        return user;
    }

    private static Forum AddForum(SneklDbContext db, User owner)
    {
        var forum = new Forum
        {
            Name = "General",
            Slug = "general",
            Description = "",
            CreatedByUserId = owner.Id
        };
        db.Forums.Add(forum);
        return forum;
    }

    private static SneklDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<SneklDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
