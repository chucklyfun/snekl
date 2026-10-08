using Snekl.Domain.Enums;

namespace Snekl.Domain;

public static class PermissionClip
{
    public static ServerPermissions ParseServer(IEnumerable<string>? names)
    {
        var flags = ServerPermissions.None;
        foreach (var name in names ?? [])
        {
            if (Enum.TryParse<ServerPermissions>(name, true, out var flag))
                flags |= flag;
        }
        return flags;
    }

    public static ForumPermissions ParseForum(IEnumerable<string>? names)
    {
        var flags = ForumPermissions.None;
        foreach (var name in names ?? [])
        {
            if (Enum.TryParse<ForumPermissions>(name, true, out var flag))
                flags |= flag;
        }
        return flags;
    }

    public static ServerPermissions Intersect(ServerPermissions requested, ServerPermissions allowed) =>
        requested & allowed;

    public static ForumPermissions Intersect(ForumPermissions requested, ForumPermissions allowed) =>
        requested & allowed;
}
