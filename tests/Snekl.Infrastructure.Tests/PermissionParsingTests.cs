using Snekl.Domain.Enums;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class PermissionParsingTests
{
    [Fact]
    public void ParseServerPermissions_Null_ReturnsNone()
    {
        Assert.Equal(ServerPermissions.None, PermissionParsing.ParseServerPermissions(null));
    }

    [Fact]
    public void ParseServerPermissions_CombinesFlags()
    {
        var perms = PermissionParsing.ParseServerPermissions(["ViewServer", "CreateForum"]);
        Assert.True(perms.HasFlag(ServerPermissions.ViewServer));
        Assert.True(perms.HasFlag(ServerPermissions.CreateForum));
    }

    [Fact]
    public void ParseServerPermissions_AdministratorImpliesManageFlags()
    {
        var perms = PermissionParsing.ParseServerPermissions(["ServerAdministrator"]);
        Assert.True(perms.HasFlag(ServerPermissions.ServerAdministrator));
        Assert.True(perms.HasFlag(ServerPermissions.CreateForum));
        Assert.True(perms.HasFlag(ServerPermissions.ManageServerMembers));
        Assert.True(perms.HasFlag(ServerPermissions.ManageServerRoles));
        Assert.True(perms.HasFlag(ServerPermissions.ManageServerSettings));
        Assert.True(perms.HasFlag(ServerPermissions.RequestFederation));
        Assert.True(perms.HasFlag(ServerPermissions.AcceptFederation));
        Assert.True(perms.HasFlag(ServerPermissions.ManageContentBlocks));
        Assert.True(perms.HasFlag(ServerPermissions.CreateWatercooler));
        Assert.True(perms.HasFlag(ServerPermissions.ViewHistory));
        Assert.False(perms.HasFlag(ServerPermissions.RequestCreateForum));
    }

    [Fact]
    public void ParseServerModes_RequestCreateForum_MapsToCreateForumRequest()
    {
        var modes = PermissionParsing.ParseServerModes(["ViewServer", "RequestCreateForum"]);
        Assert.Equal(GrantMode.Direct, PermissionModeMap.Get(modes, ServerPermissions.ViewServer));
        Assert.Equal(GrantMode.Request, PermissionModeMap.Get(modes, ServerPermissions.CreateForum));
        Assert.False(modes.ContainsKey(nameof(ServerPermissions.RequestCreateForum)));
    }

    [Fact]
    public void ParseForumPermissions_AdministratorImpliesBaseFlags()
    {
        var perms = PermissionParsing.ParseForumPermissions(["ForumAdministrator"]);
        Assert.True(perms.HasFlag(ForumPermissions.ForumAdministrator));
        Assert.True(perms.HasFlag(ForumPermissions.ViewForum));
        Assert.True(perms.HasFlag(ForumPermissions.CreateThread));
        Assert.True(perms.HasFlag(ForumPermissions.ManageForum));
        Assert.True(perms.HasFlag(ForumPermissions.SeeContentBlocks));
        Assert.True(perms.HasFlag(ForumPermissions.ManageContentBlocks));
        Assert.True(perms.HasFlag(ForumPermissions.ViewNsfwMedia));
        Assert.True(perms.HasFlag(ForumPermissions.MarkNsfwMedia));
        Assert.True(perms.HasFlag(ForumPermissions.RequestDirectMessage));
        Assert.True(perms.HasFlag(ForumPermissions.ViewHistory));
    }

    [Fact]
    public void ParseForumPermissions_IgnoresUnknownNames()
    {
        var perms = PermissionParsing.ParseForumPermissions(["ViewForum", "NotARealPermission"]);
        Assert.True(perms.HasFlag(ForumPermissions.ViewForum));
        Assert.True(perms.HasFlag(ForumPermissions.SeeForum));
    }

    [Fact]
    public void ParseForumPermissions_AdministratorImpliesSeeForum()
    {
        var perms = PermissionParsing.ParseForumPermissions(["ForumAdministrator"]);
        Assert.True(perms.HasFlag(ForumPermissions.SeeForum));
    }

    [Fact]
    public void ParseForumModes_KeepsCatalogNames()
    {
        var extras = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ManageCalendar" };
        var modes = PermissionParsing.ParseForumModes(["ViewForum", "ManageCalendar", "NotARealPermission"], extras);
        Assert.Equal(GrantMode.Direct, PermissionModeMap.Get(modes, ForumPermissions.ViewForum));
        Assert.Equal(GrantMode.Direct, PermissionModeMap.Get(modes, ForumPermissions.SeeContentBlocks));
        Assert.Equal(GrantMode.Direct, PermissionModeMap.Get(modes, "ManageCalendar"));
        Assert.False(modes.ContainsKey("NotARealPermission"));
    }
}
