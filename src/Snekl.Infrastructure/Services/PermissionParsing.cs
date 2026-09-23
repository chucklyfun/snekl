using Snekl.Domain.Enums;

namespace Snekl.Infrastructure.Services;

public static class PermissionParsing
{
    /// <summary>
    /// Parses wire permission names into a mode map.
    /// Bare names (CreateForum) → Direct; RequestCreateForum / Request{Name} → Request on the base permission.
    /// </summary>
    public static Dictionary<string, GrantMode> ParseServerModes(
        IEnumerable<string>? names,
        IReadOnlySet<string>? extraNames = null)
    {
        var result = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase);
        if (names is null) return result;

        foreach (var raw in names)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var name = raw.Trim();

            if (name.Equals(nameof(ServerPermissions.RequestCreateForum), StringComparison.OrdinalIgnoreCase))
            {
                SetMode(result, nameof(ServerPermissions.CreateForum), GrantMode.Request);
                continue;
            }

            if (name.StartsWith("Request", StringComparison.OrdinalIgnoreCase) &&
                name.Length > "Request".Length)
            {
                var baseName = name["Request".Length..];
                if (Enum.TryParse<ServerPermissions>(baseName, true, out var requested) &&
                    requested != ServerPermissions.None)
                {
                    SetMode(result, requested.ToString(), GrantMode.Request);
                    continue;
                }

                if (IsExtra(extraNames, baseName))
                {
                    SetMode(result, CanonicalExtra(extraNames, baseName), GrantMode.Request);
                    continue;
                }
            }

            if (Enum.TryParse<ServerPermissions>(name, true, out var flag) && flag != ServerPermissions.None)
                SetMode(result, flag.ToString(), GrantMode.Direct);
            else if (IsExtra(extraNames, name))
                SetMode(result, CanonicalExtra(extraNames, name), GrantMode.Direct);
        }

        if (PermissionModeMap.Get(result, ServerPermissions.ServerAdministrator) == GrantMode.Direct)
            PermissionModeMap.ApplyServerAdministratorImplied(result);

        return result;
    }

    public static Dictionary<string, GrantMode> ParseForumModes(
        IEnumerable<string>? names,
        IReadOnlySet<string>? extraNames = null)
    {
        var result = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase);
        if (names is null) return result;

        foreach (var raw in names)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var name = raw.Trim();

            if (name.StartsWith("Request", StringComparison.OrdinalIgnoreCase) &&
                name.Length > "Request".Length)
            {
                var baseName = name["Request".Length..];
                if (Enum.TryParse<ForumPermissions>(baseName, true, out var requested) &&
                    requested != ForumPermissions.None)
                {
                    SetMode(result, requested.ToString(), GrantMode.Request);
                    continue;
                }

                if (IsExtra(extraNames, baseName))
                {
                    SetMode(result, CanonicalExtra(extraNames, baseName), GrantMode.Request);
                    continue;
                }
            }

            if (Enum.TryParse<ForumPermissions>(name, true, out var flag) && flag != ForumPermissions.None)
                SetMode(result, flag.ToString(), GrantMode.Direct);
            else if (IsExtra(extraNames, name))
                SetMode(result, CanonicalExtra(extraNames, name), GrantMode.Direct);
        }

        if (PermissionModeMap.Get(result, ForumPermissions.ForumAdministrator) == GrantMode.Direct)
            PermissionModeMap.ApplyForumAdministratorImplied(result);

        if (PermissionModeMap.AtLeast(result, ForumPermissions.ViewForum, GrantMode.Direct) &&
            PermissionModeMap.Get(result, ForumPermissions.SeeForum) == GrantMode.None)
        {
            result[nameof(ForumPermissions.SeeForum)] = GrantMode.Direct;
        }

        if (PermissionModeMap.AtLeast(result, ForumPermissions.ViewForum, GrantMode.Direct) &&
            PermissionModeMap.Get(result, ForumPermissions.SeeContentBlocks) == GrantMode.None)
        {
            result[nameof(ForumPermissions.SeeContentBlocks)] = GrantMode.Direct;
        }

        return result;
    }

    public static Dictionary<string, GrantMode> ParseWatercoolerModes(IEnumerable<string>? names)
    {
        var result = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase);
        if (names is null) return result;

        foreach (var raw in names)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var name = raw.Trim();
            if (Enum.TryParse<WatercoolerPermissions>(name, true, out var flag) && flag != WatercoolerPermissions.None)
                SetMode(result, flag.ToString(), GrantMode.Direct);
        }

        if (PermissionModeMap.Get(result, WatercoolerPermissions.WatercoolerAdministrator) == GrantMode.Direct)
            PermissionModeMap.ApplyWatercoolerAdministratorImplied(result);

        if (PermissionModeMap.AtLeast(result, WatercoolerPermissions.ViewWatercooler, GrantMode.Direct) &&
            PermissionModeMap.Get(result, WatercoolerPermissions.SeeWatercooler) == GrantMode.None)
        {
            result[nameof(WatercoolerPermissions.SeeWatercooler)] = GrantMode.Direct;
        }

        return result;
    }

    public static ServerPermissions ParseServerPermissions(IEnumerable<string>? names) =>
        PermissionModeMap.ToServerFlags(ParseServerModes(names));

    public static ForumPermissions ParseForumPermissions(IEnumerable<string>? names) =>
        PermissionModeMap.ToForumFlags(ParseForumModes(names));

    private static void SetMode(Dictionary<string, GrantMode> modes, string permission, GrantMode mode)
    {
        if (!modes.TryGetValue(permission, out var existing) || mode > existing)
            modes[permission] = mode;
    }

    private static bool IsExtra(IReadOnlySet<string>? extraNames, string name) =>
        extraNames is not null && extraNames.Contains(name);

    private static string CanonicalExtra(IReadOnlySet<string>? extraNames, string name)
    {
        if (extraNames is null) return name;
        foreach (var extra in extraNames)
        {
            if (extra.Equals(name, StringComparison.OrdinalIgnoreCase))
                return extra;
        }

        return name;
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
