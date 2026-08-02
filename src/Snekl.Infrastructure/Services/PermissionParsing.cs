using Snekl.Domain.Enums;

namespace Snekl.Infrastructure.Services;

public static class PermissionParsing
{
    public static ServerPermissions ParseServerPermissions(IEnumerable<string>? names)
    {
        if (names is null) return ServerPermissions.None;
        var result = ServerPermissions.None;
        foreach (var name in names)
        {
            if (Enum.TryParse<ServerPermissions>(name, true, out var flag))
                result |= flag;
        }
        if (result.HasFlag(ServerPermissions.ServerAdministrator))
            result |= ServerPermissions.CreateForum |
                      ServerPermissions.ManageServerMembers | ServerPermissions.ManageServerRoles |
                      ServerPermissions.ManageServerSettings;
        return result;
    }

    public static ForumPermissions ParseForumPermissions(IEnumerable<string>? names)
    {
        if (names is null) return ForumPermissions.None;
        var result = ForumPermissions.None;
        foreach (var name in names)
        {
            if (Enum.TryParse<ForumPermissions>(name, true, out var flag))
                result |= flag;
        }
        if (result.HasFlag(ForumPermissions.ForumAdministrator))
            result |= ForumPermissions.ViewForum | ForumPermissions.CreateThread | ForumPermissions.Reply |
                      ForumPermissions.EditOwnPost | ForumPermissions.DeleteOwnPost | ForumPermissions.ManagePosts |
                      ForumPermissions.ManageMembers | ForumPermissions.ManageRoles | ForumPermissions.ManageForum;
        return result;
    }
}

public class PermissionDeniedException : Exception
{
    public string RequiredPermission { get; }
    public string Scope { get; }

    public PermissionDeniedException(string requiredPermission, string scope)
        : base($"Missing permission: {requiredPermission}")
    {
        RequiredPermission = requiredPermission;
        Scope = scope;
    }
}
