using Microsoft.EntityFrameworkCore;
using Snekl.Domain;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class PermissionServiceTests
{
    [Fact]
    public async Task GetServerPermissions_UsesEveryoneWhenUnauthenticated()
    {
        await using var db = CreateDb();
        db.ServerRoles.Add(new ServerRole
        {
            Name = "@everyone",
            IsEveryone = true,
            Position = 0,
            Modes = PermissionModeMap.FromServerFlags(ServerPermissions.ViewServer)
        });
        await db.SaveChangesAsync();

        var service = new PermissionService(db);
        var perms = await service.GetServerPermissionsAsync(null);

        Assert.Equal(ServerPermissions.ViewServer, perms);
    }

    [Fact]
    public async Task GetServerPermissions_MergesAssignedRoles()
    {
        await using var db = CreateDb();
        var user = new User
        {
            Username = "bob",
            DisplayName = "Bob",
            Email = "bob@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        var everyone = new ServerRole
        {
            Name = "@everyone",
            IsEveryone = true,
            Position = 0,
            Modes = PermissionModeMap.FromServerFlags(ServerPermissions.ViewServer)
        };
        var member = new ServerRole
        {
            Name = "Member",
            Position = 1,
            Modes = PermissionModeMap.FromServerFlags(ServerPermissions.CreateForum)
        };
        db.ServerRoles.AddRange(everyone, member);
        await db.SaveChangesAsync();
        db.ServerRoleAssignments.Add(new ServerRoleAssignment { UserId = user.Id, RoleId = member.Id });
        await db.SaveChangesAsync();

        var service = new PermissionService(db);
        var perms = await service.GetServerPermissionsAsync(user.Id);

        Assert.True(perms.HasFlag(ServerPermissions.ViewServer));
        Assert.True(perms.HasFlag(ServerPermissions.CreateForum));
    }

    [Fact]
    public async Task CreateForumRequestMode_SurfacesAsRequestCreateForumFlag()
    {
        await using var db = CreateDb();
        var user = new User
        {
            Username = "req",
            DisplayName = "Req",
            Email = "req@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        var everyone = new ServerRole
        {
            Name = "@everyone",
            IsEveryone = true,
            Position = 0,
            Modes = PermissionModeMap.FromServerFlags(ServerPermissions.ViewServer)
        };
        var requester = new ServerRole
        {
            Name = "Forum requester",
            Position = 1,
            Modes = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(ServerPermissions.ViewServer)] = GrantMode.Direct,
                [nameof(ServerPermissions.CreateForum)] = GrantMode.Request
            }
        };
        db.ServerRoles.AddRange(everyone, requester);
        await db.SaveChangesAsync();
        db.ServerRoleAssignments.Add(new ServerRoleAssignment { UserId = user.Id, RoleId = requester.Id });
        await db.SaveChangesAsync();

        var service = new PermissionService(db);
        var perms = await service.GetServerPermissionsAsync(user.Id);
        var modes = await service.GetServerPermissionModesAsync(user.Id);

        Assert.Equal(GrantMode.Request, PermissionModeMap.Get(modes, ServerPermissions.CreateForum));
        Assert.True(perms.HasFlag(ServerPermissions.RequestCreateForum));
        Assert.False(perms.HasFlag(ServerPermissions.CreateForum));
    }

    [Fact]
    public async Task ChildForum_DoesNotInheritParentViewViaOrMerge()
    {
        await using var db = CreateDb();
        db.ServerRoles.Add(new ServerRole
        {
            Name = "@everyone",
            IsEveryone = true,
            Position = 0,
            Modes = PermissionModeMap.FromServerFlags(ServerPermissions.ViewServer)
        });

        var parent = new Forum
        {
            Name = "Parent",
            Slug = "parent",
            Description = "open",
            CreatedByUserId = Guid.NewGuid()
        };
        var child = new Forum
        {
            Name = "Child",
            Slug = "child",
            Description = "private",
            CreatedByUserId = parent.CreatedByUserId,
            ParentForumId = parent.Id
        };
        // Parent Id must be set before child FK — assign explicitly
        parent.Id = Guid.NewGuid();
        child.Id = Guid.NewGuid();
        child.ParentForumId = parent.Id;
        db.Forums.AddRange(parent, child);

        db.ForumRoles.Add(new ForumRole
        {
            ForumId = parent.Id,
            Name = "@everyone",
            IsEveryone = true,
            Position = 0,
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum |
                ForumPermissions.CreateThread | ForumPermissions.Reply)
        });
        db.ForumRoles.Add(new ForumRole
        {
            ForumId = child.Id,
            Name = "@everyone",
            IsEveryone = true,
            Position = 0,
            Modes = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase)
            {
                // Listed but no View — Phase 2 redacted; Phase 1 CanView is false
                [nameof(ForumPermissions.SeeForum)] = GrantMode.Direct
            }
        });
        await db.SaveChangesAsync();

        var service = new PermissionService(db);

        Assert.True(await service.CanSeeForumAsync(null, parent.Id));
        Assert.True(await service.CanViewForumAsync(null, parent.Id));
        Assert.True(await service.CanSeeForumAsync(null, child.Id));
        Assert.False(await service.CanViewForumAsync(null, child.Id));
    }

    [Fact]
    public async Task ChildForum_HiddenWhenParentNotSeeable()
    {
        await using var db = CreateDb();
        db.ServerRoles.Add(new ServerRole
        {
            Name = "@everyone",
            IsEveryone = true,
            Position = 0,
            Modes = PermissionModeMap.FromServerFlags(ServerPermissions.ViewServer)
        });

        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        db.Forums.Add(new Forum
        {
            Id = parentId,
            Name = "Secret",
            Slug = "secret",
            Description = "speakeasy",
            CreatedByUserId = Guid.NewGuid()
        });
        db.Forums.Add(new Forum
        {
            Id = childId,
            Name = "Open child",
            Slug = "open-child",
            Description = "would be open",
            CreatedByUserId = Guid.NewGuid(),
            ParentForumId = parentId
        });

        db.ForumRoles.Add(new ForumRole
        {
            ForumId = parentId,
            Name = "@everyone",
            IsEveryone = true,
            Position = 0,
            Modes = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase)
        });
        db.ForumRoles.Add(new ForumRole
        {
            ForumId = childId,
            Name = "@everyone",
            IsEveryone = true,
            Position = 0,
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum)
        });
        await db.SaveChangesAsync();

        var service = new PermissionService(db);
        Assert.False(await service.CanSeeForumAsync(null, parentId));
        Assert.False(await service.CanSeeForumAsync(null, childId));
        Assert.False(await service.CanViewForumAsync(null, childId));
    }

    [Fact]
    public async Task IsSuperUser_ReturnsTrueWhenGranted()
    {
        await using var db = CreateDb();
        var user = new User
        {
            Username = "su",
            DisplayName = "SU",
            Email = "su@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.SuperUserGrants.Add(new SuperUserGrant
        {
            UserId = user.Id,
            GrantedBySiteAdminId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();

        var service = new PermissionService(db);
        Assert.True(await service.IsSuperUserAsync(user.Id));
        Assert.False(await service.IsSuperUserAsync(Guid.NewGuid()));

        var perms = await service.GetServerPermissionsAsync(user.Id);
        Assert.True(perms.HasFlag(ServerPermissions.CreateForum));
        Assert.True(perms.HasFlag(ServerPermissions.ManageServerRoles));
    }

    [Fact]
    public async Task HasForumRoleAssignment_TrueOnlyWhenAssigned()
    {
        await using var db = CreateDb();
        var user = AddUser(db, "mem");
        var lurker = AddUser(db, "lurk");
        var forumId = Guid.NewGuid();
        db.Forums.Add(new Forum
        {
            Id = forumId,
            Name = "Garden",
            Slug = "garden",
            Description = "public",
            CreatedByUserId = user.Id
        });
        var everyone = new ForumRole
        {
            ForumId = forumId,
            Name = "@everyone",
            IsEveryone = true,
            Position = 0,
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum)
        };
        var member = new ForumRole
        {
            ForumId = forumId,
            Name = "Member",
            Position = 1,
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum |
                ForumPermissions.CreateThread)
        };
        db.ForumRoles.AddRange(everyone, member);
        await db.SaveChangesAsync();
        db.ForumRoleAssignments.Add(new ForumRoleAssignment
        {
            ForumId = forumId,
            UserId = user.Id,
            RoleId = member.Id
        });
        await db.SaveChangesAsync();

        var service = new PermissionService(db);
        Assert.True(await service.HasForumRoleAssignmentAsync(user.Id, forumId));
        Assert.False(await service.HasForumRoleAssignmentAsync(lurker.Id, forumId));
        Assert.False(await service.HasForumRoleAssignmentAsync(null, forumId));
    }

    [Fact]
    public async Task ViewerCanManageFromRoles_IgnoresSuperUserGodMode()
    {
        await using var db = CreateDb();
        var su = AddUser(db, "su2");
        var owner = AddUser(db, "own");
        var forumId = Guid.NewGuid();
        db.Forums.Add(new Forum
        {
            Id = forumId,
            Name = "Staff",
            Slug = "staff",
            Description = "managed",
            CreatedByUserId = owner.Id
        });
        db.SuperUserGrants.Add(new SuperUserGrant
        {
            UserId = su.Id,
            GrantedBySiteAdminId = Guid.NewGuid()
        });
        var everyone = new ForumRole
        {
            ForumId = forumId,
            Name = "@everyone",
            IsEveryone = true,
            Position = 0,
            Modes = PermissionModeMap.FromForumFlags(
                ForumPermissions.SeeForum | ForumPermissions.ViewForum)
        };
        var ownerRole = new ForumRole
        {
            ForumId = forumId,
            Name = "Owner",
            Position = 100,
            Modes = PermissionModeMap.FromForumFlags(ForumPermissions.ForumAdministrator)
        };
        db.ForumRoles.AddRange(everyone, ownerRole);
        await db.SaveChangesAsync();
        db.ForumRoleAssignments.Add(new ForumRoleAssignment
        {
            ForumId = forumId,
            UserId = owner.Id,
            RoleId = ownerRole.Id
        });
        await db.SaveChangesAsync();

        var service = new PermissionService(db);
        Assert.True(await service.IsSuperUserAsync(su.Id));
        Assert.True(await service.CanViewForumAsync(su.Id, forumId));
        Assert.False(await service.HasForumRoleAssignmentAsync(su.Id, forumId));
        Assert.False(await service.ViewerCanManageFromRolesAsync(su.Id, forumId));
        Assert.True(await service.ViewerCanManageFromRolesAsync(owner.Id, forumId));
    }

    [Fact]
    public async Task BotInstall_CapsPermissionsToGrantAndForum()
    {
        await using var db = CreateDb();
        var bot = AddUser(db, "botmira");
        bot.IsBot = true;
        var forum = new Forum
        {
            Name = "Hotel",
            Slug = "hotel",
            Description = "",
            CreatedByUserId = bot.Id
        };
        var other = new Forum
        {
            Name = "Other",
            Slug = "other",
            Description = "",
            CreatedByUserId = bot.Id
        };
        db.Forums.AddRange(forum, other);
        await db.SaveChangesAsync();
        var install = new ExtensionInstall
        {
            ClientId = "snekl-faker",
            AppName = "Faker",
            InstalledByUserId = bot.Id,
            ResourceType = ExtensionResourceTypes.Forum,
            ForumId = forum.Id,
            ServerPermissions = ServerPermissions.ViewServer,
            ForumPermissions = ForumPermissions.ViewForum | ForumPermissions.CreateThread | ForumPermissions.Reply
        };
        db.ExtensionInstalls.Add(install);
        await db.SaveChangesAsync();
        bot.ExtensionInstallId = install.Id;
        await db.SaveChangesAsync();

        var service = new PermissionService(db);
        Assert.False(await service.IsSuperUserAsync(bot.Id));
        Assert.Equal(ServerPermissions.ViewServer, await service.GetServerPermissionsAsync(bot.Id));
        var inForum = await service.GetForumPermissionsAsync(bot.Id, forum.Id);
        Assert.True(inForum.HasFlag(ForumPermissions.CreateThread));
        Assert.False(inForum.HasFlag(ForumPermissions.ManagePosts));
        var otherPerms = await service.GetForumPermissionsAsync(bot.Id, other.Id);
        Assert.False(otherPerms.HasFlag(ForumPermissions.CreateThread));
        await service.EnsureBotMayAttachMediaAsync(bot.Id);

        var viewerOnly = new User
        {
            Username = "viewbot",
            DisplayName = "viewbot",
            Email = "viewbot@example.com",
            PasswordHash = "x",
            IsBot = true
        };
        db.Users.Add(viewerOnly);
        await db.SaveChangesAsync();
        var viewInstall = new ExtensionInstall
        {
            ClientId = "view-ext",
            AppName = "View",
            InstalledByUserId = viewerOnly.Id,
            ResourceType = ExtensionResourceTypes.Forum,
            ForumId = forum.Id,
            ServerPermissions = ServerPermissions.ViewServer,
            ForumPermissions = ForumPermissions.ViewForum | ForumPermissions.SeeForum
        };
        db.ExtensionInstalls.Add(viewInstall);
        await db.SaveChangesAsync();
        viewerOnly.ExtensionInstallId = viewInstall.Id;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<PermissionDeniedException>(() => service.EnsureBotMayAttachMediaAsync(viewerOnly.Id));
    }

    [Fact]
    public async Task NestedWatercooler_RequiresParentSeeAndView()
    {
        await using var db = CreateDb();
        db.ServerRoles.Add(new ServerRole
        {
            Name = "@everyone",
            IsEveryone = true,
            Modes = PermissionModeMap.FromServerFlags(ServerPermissions.ViewServer)
        });
        var parent = new Forum
        {
            Name = "Hidden",
            Slug = "hidden",
            Description = "",
            CreatedByUserId = Guid.NewGuid()
        };
        db.Forums.Add(parent);
        db.ForumRoles.Add(new ForumRole
        {
            ForumId = parent.Id,
            Name = "@everyone",
            IsEveryone = true,
            Modes = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase)
        });
        await db.SaveChangesAsync();

        var cooler = new Watercooler
        {
            Name = "Lounge",
            Slug = "lounge",
            Description = "",
            CreatedByUserId = parent.CreatedByUserId,
            ParentForumId = parent.Id,
            ApprovalStatus = ForumApprovalStatuses.Approved
        };
        db.Watercoolers.Add(cooler);
        await db.SaveChangesAsync();
        db.WatercoolerRoles.Add(new WatercoolerRole
        {
            WatercoolerId = cooler.Id,
            Name = "@everyone",
            IsEveryone = true,
            Modes = PermissionModeMap.FromWatercoolerFlags(
                WatercoolerPermissions.SeeWatercooler |
                WatercoolerPermissions.ViewWatercooler |
                WatercoolerPermissions.SpeakWatercooler)
        });
        await db.SaveChangesAsync();

        var service = new PermissionService(db);
        Assert.False(await service.CanSeeWatercoolerAsync(null, cooler.Id));
        Assert.False(await service.CanViewWatercoolerAsync(null, cooler.Id));
        Assert.False(await service.CanSpeakWatercoolerAsync(null, cooler.Id));
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

    private static SneklDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SneklDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SneklDbContext(options);
    }
}
