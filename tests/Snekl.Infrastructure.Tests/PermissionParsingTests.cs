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
    }

    [Fact]
    public void ParseForumPermissions_AdministratorImpliesBaseFlags()
    {
        var perms = PermissionParsing.ParseForumPermissions(["ForumAdministrator"]);
        Assert.True(perms.HasFlag(ForumPermissions.ForumAdministrator));
        Assert.True(perms.HasFlag(ForumPermissions.ViewForum));
        Assert.True(perms.HasFlag(ForumPermissions.CreateThread));
        Assert.True(perms.HasFlag(ForumPermissions.ManageForum));
    }

    [Fact]
    public void ParseForumPermissions_IgnoresUnknownNames()
    {
        var perms = PermissionParsing.ParseForumPermissions(["ViewForum", "NotARealPermission"]);
        Assert.Equal(ForumPermissions.ViewForum, perms);
    }
}
