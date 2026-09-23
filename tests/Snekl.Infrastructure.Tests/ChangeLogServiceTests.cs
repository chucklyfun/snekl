using Microsoft.EntityFrameworkCore;
using Snekl.Domain;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class ChangeLogServiceTests
{
    [Fact]
    public async Task UpdateForumRole_RecordsHistoryEntry()
    {
        await using var db = CreateDb();
        var actor = AddUser(db, "mod");
        var forum = AddForum(db);
        var role = new ForumRole
        {
            ForumId = forum.Id,
            Name = "Moderator",
            Position = 50,
            Modes = PermissionModeMap.FromForumFlags(ForumPermissions.ManagePosts)
        };
        db.ForumRoles.Add(role);
        await db.SaveChangesAsync();

        var changeLog = new ChangeLogService(db);
        var roles = new ForumRoleService(db, new PermissionService(db), changeLog: changeLog);
        await roles.UpdateRoleAsync(
            forum.Id,
            role.Id,
            new UpdateForumRoleRequest("Staff", "#faa61a", 50, ["SeeForum", "ViewForum", "ViewHistory"]),
            actor.Id);

        var rows = await changeLog.ListForumAsync(forum.Id);
        Assert.Single(rows);
        Assert.Equal(ChangeLogActions.Updated, rows[0].Action);
        Assert.Equal(ChangeLogEntityTypes.ForumRole, rows[0].EntityType);
        Assert.Equal("mod", rows[0].ActorUsername);
        Assert.Contains("Staff", rows[0].Summary);
        Assert.Contains("ViewHistory", rows[0].AfterJson);
    }

    [Fact]
    public async Task SeededModerator_HasViewHistory()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var forum = AddForum(db);
        await db.SaveChangesAsync();

        await new RoleSeedService(db).SeedForumRolesAsync(forum.Id, owner.Id);
        var moderator = await db.ForumRoles.SingleAsync(r => r.ForumId == forum.Id && r.Name == "Moderator");
        Assert.Equal(GrantMode.Direct, PermissionModeMap.Get(moderator.Modes, ForumPermissions.ViewHistory));
    }

    [Fact]
    public async Task UpdateWatercooler_RecordsHistoryWhenLoggerProvided()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner");
        var forum = AddForum(db);
        db.ForumRoles.Add(new ForumRole
        {
            ForumId = forum.Id,
            Name = "@everyone",
            IsEveryone = true,
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum | ForumPermissions.ManageForum)
        });
        await db.SaveChangesAsync();

        var changeLog = new ChangeLogService(db);
        var coolerService = new WatercoolerService(db, new PermissionService(db), new RoleSeedService(db), new NullPresentation(), changeLog);
        var cooler = await coolerService.CreateAsync(owner.Id, new CreateWatercoolerRequest("Chat", "chat", "", forum.Id));
        await coolerService.UpdateAsync(owner.Id, cooler.Id, new UpdateWatercoolerRequest(Name: "Lounge"));

        var rows = await changeLog.ListWatercoolerAsync(cooler.Id);
        Assert.Contains(rows, r => r.Action == ChangeLogActions.Updated && r.Summary.Contains("Lounge"));
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

    private static Forum AddForum(SneklDbContext db)
    {
        var forum = new Forum
        {
            Name = "General",
            Slug = "general",
            Description = "d",
            CreatedByUserId = Guid.NewGuid()
        };
        db.Forums.Add(forum);
        return forum;
    }

    private static SneklDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<SneklDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class NullPresentation : IUserPresentationService
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
