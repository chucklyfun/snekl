namespace Snekl.Domain.Enums;

[Flags]
public enum ServerPermissions : ulong
{
    None = 0,
    ViewServer = 1UL << 0,
    CreateForum = 1UL << 1,
    ManageServerMembers = 1UL << 2,
    ManageServerRoles = 1UL << 3,
    ManageServerSettings = 1UL << 4,
    ServerAdministrator = 1UL << 5
}

[Flags]
public enum ForumPermissions : ulong
{
    None = 0,
    ViewForum = 1UL << 0,
    CreateThread = 1UL << 1,
    Reply = 1UL << 2,
    EditOwnPost = 1UL << 3,
    DeleteOwnPost = 1UL << 4,
    ManagePosts = 1UL << 5,
    ManageMembers = 1UL << 6,
    ManageRoles = 1UL << 7,
    ManageForum = 1UL << 8,
    ForumAdministrator = 1UL << 9
}

public static class PermissionNames
{
    public static IReadOnlyList<string> ToNames(ServerPermissions perms) =>
        Enum.GetValues<ServerPermissions>()
            .Where(p => p != ServerPermissions.None && p != ServerPermissions.ServerAdministrator && perms.HasFlag(p))
            .Select(p => p.ToString())
            .ToList();

    public static IReadOnlyList<string> ToNames(ForumPermissions perms) =>
        Enum.GetValues<ForumPermissions>()
            .Where(p => p != ForumPermissions.None && p != ForumPermissions.ForumAdministrator && perms.HasFlag(p))
            .Select(p => p.ToString())
            .ToList();
}
