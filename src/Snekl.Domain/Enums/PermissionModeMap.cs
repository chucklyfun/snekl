namespace Snekl.Domain.Enums;

/// <summary>
/// Helpers for permission → GrantMode maps. Missing key means <see cref="GrantMode.None"/>.
/// </summary>
public static class PermissionModeMap
{
    public static GrantMode Get(IReadOnlyDictionary<string, GrantMode>? modes, string permission)
    {
        if (modes is null) return GrantMode.None;
        return modes.TryGetValue(permission, out var mode) ? mode : GrantMode.None;
    }

    public static GrantMode Get(IReadOnlyDictionary<string, GrantMode>? modes, ServerPermissions permission) =>
        Get(modes, permission.ToString());

    public static GrantMode Get(IReadOnlyDictionary<string, GrantMode>? modes, ForumPermissions permission) =>
        Get(modes, permission.ToString());

    public static GrantMode Get(IReadOnlyDictionary<string, GrantMode>? modes, WatercoolerPermissions permission) =>
        Get(modes, permission.ToString());

    public static bool AtLeast(IReadOnlyDictionary<string, GrantMode>? modes, string permission, GrantMode minimum) =>
        Get(modes, permission) >= minimum;

    public static bool AtLeast(IReadOnlyDictionary<string, GrantMode>? modes, ServerPermissions permission, GrantMode minimum) =>
        AtLeast(modes, permission.ToString(), minimum);

    public static bool AtLeast(IReadOnlyDictionary<string, GrantMode>? modes, ForumPermissions permission, GrantMode minimum) =>
        AtLeast(modes, permission.ToString(), minimum);

    public static bool AtLeast(IReadOnlyDictionary<string, GrantMode>? modes, WatercoolerPermissions permission, GrantMode minimum) =>
        AtLeast(modes, permission.ToString(), minimum);

    public static Dictionary<string, GrantMode> MergeMax(params IReadOnlyDictionary<string, GrantMode>?[] maps)
    {
        var result = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in maps)
        {
            if (map is null) continue;
            foreach (var (key, mode) in map)
            {
                if (mode == GrantMode.None) continue;
                if (!result.TryGetValue(key, out var existing) || mode > existing)
                    result[key] = mode;
            }
        }
        return result;
    }

    public static Dictionary<string, GrantMode> FromServerFlags(ServerPermissions flags)
    {
        var result = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase);
        if (flags.HasFlag(ServerPermissions.RequestCreateForum))
        {
            result[nameof(ServerPermissions.CreateForum)] = GrantMode.Request;
            flags &= ~ServerPermissions.RequestCreateForum;
        }

        foreach (var flag in Enum.GetValues<ServerPermissions>())
        {
            if (flag is ServerPermissions.None or ServerPermissions.RequestCreateForum) continue;
            if (!flags.HasFlag(flag)) continue;
            var name = flag.ToString();
            if (flag == ServerPermissions.CreateForum && result.TryGetValue(name, out var existing) && existing == GrantMode.Request)
                continue;
            result[name] = GrantMode.Direct;
        }

        if (result.TryGetValue(nameof(ServerPermissions.ServerAdministrator), out var admin) && admin == GrantMode.Direct)
            ApplyServerAdministratorImplied(result);

        return result;
    }

    public static Dictionary<string, GrantMode> FromForumFlags(ForumPermissions flags)
    {
        var result = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase);
        foreach (var flag in Enum.GetValues<ForumPermissions>())
        {
            if (flag == ForumPermissions.None) continue;
            if (!flags.HasFlag(flag)) continue;
            result[flag.ToString()] = GrantMode.Direct;
        }

        if (result.TryGetValue(nameof(ForumPermissions.ForumAdministrator), out var admin) && admin == GrantMode.Direct)
            ApplyForumAdministratorImplied(result);

        // Open forums historically had ViewForum without SeeForum — treat Direct View as Direct See.
        if (AtLeast(result, ForumPermissions.ViewForum, GrantMode.Direct) &&
            Get(result, ForumPermissions.SeeForum) == GrantMode.None)
        {
            result[nameof(ForumPermissions.SeeForum)] = GrantMode.Direct;
        }

        if (AtLeast(result, ForumPermissions.ViewForum, GrantMode.Direct) &&
            Get(result, ForumPermissions.SeeContentBlocks) == GrantMode.None)
        {
            result[nameof(ForumPermissions.SeeContentBlocks)] = GrantMode.Direct;
        }

        return result;
    }

    public static Dictionary<string, GrantMode> FromWatercoolerFlags(WatercoolerPermissions flags)
    {
        var result = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase);
        foreach (var flag in Enum.GetValues<WatercoolerPermissions>())
        {
            if (flag == WatercoolerPermissions.None) continue;
            if (!flags.HasFlag(flag)) continue;
            result[flag.ToString()] = GrantMode.Direct;
        }

        if (result.TryGetValue(nameof(WatercoolerPermissions.WatercoolerAdministrator), out var admin) &&
            admin == GrantMode.Direct)
            ApplyWatercoolerAdministratorImplied(result);

        if (AtLeast(result, WatercoolerPermissions.ViewWatercooler, GrantMode.Direct) &&
            Get(result, WatercoolerPermissions.SeeWatercooler) == GrantMode.None)
        {
            result[nameof(WatercoolerPermissions.SeeWatercooler)] = GrantMode.Direct;
        }

        return result;
    }

    public static void ApplyServerAdministratorImplied(Dictionary<string, GrantMode> modes)
    {
        foreach (var flag in Enum.GetValues<ServerPermissions>())
        {
            if (flag is ServerPermissions.None or ServerPermissions.RequestCreateForum) continue;
            if (flag == ServerPermissions.ServerAdministrator) continue;
            modes[flag.ToString()] = GrantMode.Direct;
        }
    }

    public static void ApplyForumAdministratorImplied(Dictionary<string, GrantMode> modes)
    {
        foreach (var flag in Enum.GetValues<ForumPermissions>())
        {
            if (flag is ForumPermissions.None or ForumPermissions.ForumAdministrator) continue;
            modes[flag.ToString()] = GrantMode.Direct;
        }
    }

    public static void ApplyWatercoolerAdministratorImplied(Dictionary<string, GrantMode> modes)
    {
        foreach (var flag in Enum.GetValues<WatercoolerPermissions>())
        {
            if (flag is WatercoolerPermissions.None or WatercoolerPermissions.WatercoolerAdministrator) continue;
            modes[flag.ToString()] = GrantMode.Direct;
        }
    }

    /// <summary>
    /// Legacy Flags view: Direct → bit set; CreateForum Request → RequestCreateForum bit for wire/compat.
    /// </summary>
    public static ServerPermissions ToServerFlags(IReadOnlyDictionary<string, GrantMode>? modes)
    {
        var result = ServerPermissions.None;
        if (modes is null) return result;

        foreach (var (key, mode) in modes)
        {
            if (mode == GrantMode.None) continue;
            if (key.Equals(nameof(ServerPermissions.CreateForum), StringComparison.OrdinalIgnoreCase))
            {
                if (mode == GrantMode.Direct)
                    result |= ServerPermissions.CreateForum;
                else if (mode == GrantMode.Request)
                    result |= ServerPermissions.RequestCreateForum;
                continue;
            }

            if (mode != GrantMode.Direct) continue;
            if (Enum.TryParse<ServerPermissions>(key, true, out var flag) && flag != ServerPermissions.None)
                result |= flag;
        }

        if (result.HasFlag(ServerPermissions.ServerAdministrator))
            result |= ServerPermissionSets.AdministratorImplied;

        return result;
    }

    public static ForumPermissions ToForumFlags(IReadOnlyDictionary<string, GrantMode>? modes)
    {
        var result = ForumPermissions.None;
        if (modes is null) return result;

        foreach (var (key, mode) in modes)
        {
            if (mode != GrantMode.Direct) continue;
            if (Enum.TryParse<ForumPermissions>(key, true, out var flag) && flag != ForumPermissions.None)
                result |= flag;
        }

        if (result.HasFlag(ForumPermissions.ForumAdministrator))
            result |= ForumPermissionSets.AdministratorImplied;

        return result;
    }

    public static WatercoolerPermissions ToWatercoolerFlags(IReadOnlyDictionary<string, GrantMode>? modes)
    {
        var result = WatercoolerPermissions.None;
        if (modes is null) return result;

        foreach (var (key, mode) in modes)
        {
            if (mode != GrantMode.Direct) continue;
            if (Enum.TryParse<WatercoolerPermissions>(key, true, out var flag) && flag != WatercoolerPermissions.None)
                result |= flag;
        }

        if (result.HasFlag(WatercoolerPermissions.WatercoolerAdministrator))
            result |= WatercoolerPermissionSets.AdministratorImplied;

        return result;
    }

    public static IReadOnlyList<string> ToWireNames(IReadOnlyDictionary<string, GrantMode>? modes, bool server)
    {
        if (modes is null || modes.Count == 0) return [];

        var names = new List<string>();
        foreach (var (key, mode) in modes.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (mode == GrantMode.None) continue;
            if (key.Equals(nameof(ServerPermissions.ServerAdministrator), StringComparison.OrdinalIgnoreCase) ||
                key.Equals(nameof(ForumPermissions.ForumAdministrator), StringComparison.OrdinalIgnoreCase))
                continue;

            if (server &&
                key.Equals(nameof(ServerPermissions.CreateForum), StringComparison.OrdinalIgnoreCase) &&
                mode == GrantMode.Request)
            {
                names.Add(nameof(ServerPermissions.RequestCreateForum));
                continue;
            }

            if (mode == GrantMode.Direct)
                names.Add(NormalizePermissionName(key, server));
            else if (mode == GrantMode.Request)
                names.Add("Request" + NormalizePermissionName(key, server));
        }

        return names;
    }

    private static string NormalizePermissionName(string key, bool server)
    {
        if (server && Enum.TryParse<ServerPermissions>(key, true, out var sp))
            return sp.ToString();
        if (!server && Enum.TryParse<ForumPermissions>(key, true, out var fp))
            return fp.ToString();
        if (Enum.TryParse<WatercoolerPermissions>(key, true, out var wp))
            return wp.ToString();
        return key;
    }
}
