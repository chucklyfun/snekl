using Snekl.Domain.Enums;

namespace Snekl.Domain.Tests;

public class PermissionNamesTests
{
    [Fact]
    public void ToNames_OmitsNoneAndAdministratorFlags()
    {
        var names = PermissionNames.ToNames(
            ServerPermissions.ViewServer | ServerPermissions.CreateForum | ServerPermissions.ServerAdministrator);

        Assert.Contains("ViewServer", names);
        Assert.Contains("CreateForum", names);
        Assert.DoesNotContain("None", names);
        Assert.DoesNotContain("ServerAdministrator", names);
    }

    [Fact]
    public void ToNames_ForumPermissions_ListsGrantedFlags()
    {
        var names = PermissionNames.ToNames(ForumPermissions.ViewForum | ForumPermissions.Reply);
        Assert.Contains("ViewForum", names);
        Assert.Contains("Reply", names);
        Assert.Contains("SeeForum", names);
        Assert.DoesNotContain("None", names);
    }

    [Fact]
    public void ToNames_CreateForumRequest_EmitsRequestCreateForumAlias()
    {
        var modes = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(ServerPermissions.CreateForum)] = GrantMode.Request
        };
        var names = PermissionNames.ToNames(modes, server: true);
        Assert.Contains("RequestCreateForum", names);
        Assert.DoesNotContain("CreateForum", names);
    }
}
