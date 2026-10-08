using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class WatercoolerServiceTests
{
    [Fact]
    public async Task Create_UnderForum_RequiresManageForum()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var forum = new Forum { Name = "Art", Slug = "art", Description = "", CreatedByUserId = owner.Id };
        db.Forums.Add(forum);
        db.ForumRoles.Add(new ForumRole
        {
            ForumId = forum.Id,
            Name = "@everyone",
            IsEveryone = true,
            Modes = PermissionModeMap.FromForumFlags(ForumPermissions.SeeForum | ForumPermissions.ViewForum)
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            service.CreateAsync(owner.Id, new CreateWatercoolerRequest("Backstage", "backstage", "", forum.Id)));
    }

    [Fact]
    public async Task Create_UnderForum_WithManageForum_SeedsRolesWithoutThreadPerms()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var forum = new Forum { Name = "Art", Slug = "art", Description = "", CreatedByUserId = owner.Id };
        db.Forums.Add(forum);
        db.ForumRoles.Add(new ForumRole
        {
            ForumId = forum.Id,
            Name = "@everyone",
            IsEveryone = true,
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum | ForumPermissions.ManageForum |
                ForumPermissions.CreateThread | ForumPermissions.Reply)
        });
        await db.SaveChangesAsync();

        var cooler = await CreateService(db).CreateAsync(
            owner.Id,
            new CreateWatercoolerRequest("Backstage", "backstage", "chat", forum.Id));

        Assert.False(cooler.AllowUpvotes);
        Assert.True(cooler.NewestAtBottom);
        Assert.Equal("approved", cooler.ApprovalStatus);
        Assert.Contains("SpeakWatercooler", cooler.ViewerPermissions ?? []);
        Assert.DoesNotContain("CreateThread", cooler.ViewerPermissions ?? []);
    }

    [Fact]
    public async Task Create_UnderForum_CanEnableUpvotes()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var forum = new Forum { Name = "Art", Slug = "art", Description = "", CreatedByUserId = owner.Id };
        db.Forums.Add(forum);
        db.ForumRoles.Add(new ForumRole
        {
            ForumId = forum.Id,
            Name = "@everyone",
            IsEveryone = true,
            Modes = PermissionModeMap.FromForumFlags(ForumPermissions.ManageForum)
        });
        await db.SaveChangesAsync();

        var cooler = await CreateService(db).CreateAsync(
            owner.Id,
            new CreateWatercoolerRequest("Lounge", "lounge", "", forum.Id, true, false));

        Assert.True(cooler.AllowUpvotes);
        Assert.False(cooler.NewestAtBottom);
    }

    [Fact]
    public async Task Create_ServerLevel_PendingWhenRequestOnly()
    {
        await using var db = CreateDb();
        var user = AddUser(db, "req");
        db.ServerRoles.Add(new ServerRole
        {
            Name = "@everyone",
            IsEveryone = true,
            Modes = PermissionModeMap.FromServerFlags(ServerPermissions.ViewServer)
        });
        var requester = new ServerRole
        {
            Name = "Requester",
            Modes = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(ServerPermissions.ViewServer)] = GrantMode.Direct,
                [nameof(ServerPermissions.CreateWatercooler)] = GrantMode.Request
            }
        };
        db.ServerRoles.Add(requester);
        await db.SaveChangesAsync();
        db.ServerRoleAssignments.Add(new ServerRoleAssignment { UserId = user.Id, RoleId = requester.Id });
        await db.SaveChangesAsync();

        var cooler = await CreateService(db).CreateAsync(
            user.Id,
            new CreateWatercoolerRequest("Plaza", "plaza", ""));

        Assert.True(cooler.AllowUpvotes);
        Assert.Equal("pending", cooler.ApprovalStatus);
    }

    [Fact]
    public async Task GetBySlug_NullWhenViewerCannotSee()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var forum = new Forum { Name = "Art", Slug = "art", Description = "", CreatedByUserId = owner.Id };
        db.Forums.Add(forum);
        db.ForumRoles.Add(new ForumRole
        {
            ForumId = forum.Id,
            Name = "@everyone",
            IsEveryone = true,
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum | ForumPermissions.ManageForum)
        });
        await db.SaveChangesAsync();
        var created = await CreateService(db).CreateAsync(
            owner.Id, new CreateWatercoolerRequest("Backstage", "backstage", "", forum.Id));

        var everyone = await db.WatercoolerRoles.SingleAsync(r => r.WatercoolerId == created.Id && r.IsEveryone);
        everyone.Modes = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase);
        await db.SaveChangesAsync();

        var hidden = await CreateService(db).GetBySlugAsync("backstage", forum.Id, Guid.NewGuid());
        Assert.Null(hidden);
    }

    [Fact]
    public async Task Update_RequiresManageWatercooler()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var lurker = AddUser(db, "lurk");
        var forum = new Forum { Name = "Art", Slug = "art", Description = "", CreatedByUserId = owner.Id };
        db.Forums.Add(forum);
        db.ForumRoles.Add(new ForumRole
        {
            ForumId = forum.Id,
            Name = "@everyone",
            IsEveryone = true,
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum | ForumPermissions.ManageForum)
        });
        await db.SaveChangesAsync();
        var created = await CreateService(db).CreateAsync(
            owner.Id, new CreateWatercoolerRequest("Plaza", "plaza", "", forum.Id, true, false));

        var everyone = await db.WatercoolerRoles.SingleAsync(r => r.WatercoolerId == created.Id && r.IsEveryone);
        everyone.Modes = PermissionModeMap.FromWatercoolerFlags(
            WatercoolerPermissions.SeeWatercooler | WatercoolerPermissions.ViewWatercooler |
            WatercoolerPermissions.SpeakWatercooler);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            CreateService(db).UpdateAsync(lurker.Id, created.Id, new UpdateWatercoolerRequest(Name: "Nope")));

        var updated = await CreateService(db).UpdateAsync(
            owner.Id, created.Id, new UpdateWatercoolerRequest(AllowUpvotes: false, NewestAtBottom: true));
        Assert.False(updated.AllowUpvotes);
        Assert.True(updated.NewestAtBottom);
    }

    private static WatercoolerService CreateService(SneklDbContext db)
    {
        var permissions = new PermissionService(db);
        var seed = new RoleSeedService(db);
        var presentation = new StubPresentation();
        return new WatercoolerService(db, permissions, seed, presentation, new ForumPageService(db, permissions));
    }

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

    private static SneklDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<SneklDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class StubPresentation : IUserPresentationService
    {
        public Task ClearExpiredStatusesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<UserPresentationDto> GetPresentationAsync(Guid userId, Guid? viewerUserId = null, Guid? forumId = null, CancellationToken ct = default) =>
            Task.FromResult(new UserPresentationDto(
                userId, "u", "U", "", null, false, null, null, "U", null, null, [], null, false, null));
        public Task<UserPresentationDto?> GetPresentationByUsernameAsync(string username, Guid? viewerUserId = null, Guid? forumId = null, CancellationToken ct = default) =>
            Task.FromResult<UserPresentationDto?>(null);
        public Task<IReadOnlyDictionary<Guid, UserPresentationDto>> GetPresentationsAsync(IEnumerable<Guid> userIds, Guid? viewerUserId = null, Guid? forumId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, UserPresentationDto>>(new Dictionary<Guid, UserPresentationDto>());
        public Task InvalidatePresentationAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
