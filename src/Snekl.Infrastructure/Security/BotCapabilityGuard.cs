using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Snekl.Domain;

namespace Snekl.Infrastructure.Security;

/// <summary>
/// Bots inherit clipped install flags on forum/server APIs. Writes that are not
/// mapped to those flags (profile, lists, operator login, extra installs) are denied.
/// </summary>
public static class BotCapabilityGuard
{
    public static bool IsBlocked(HttpContext context)
    {
        if (!IsBotPrincipal(context.User))
            return false;

        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return false;

        return !IsGrantBackedWrite(context.Request.Path.Value ?? "");
    }

    public static bool IsBotPrincipal(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return false;
        if (SiteAdminAuthorization.IsSiteAdmin(user)) return false;

        var tokenUse = user.FindFirst(OAuthClaims.TokenUse)?.Value;
        if (string.Equals(tokenUse, OAuthClaims.TokenUseBot, StringComparison.Ordinal))
            return true;

        return string.Equals(user.FindFirst(OAuthClaims.Bot)?.Value, "true", StringComparison.Ordinal);
    }

    public static bool IsGrantBackedWrite(string path)
    {
        var p = Normalize(path);
        if (p.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase))
            return true;
        if (IsMemberOnlyForumPath(p))
            return false;
        if (p.StartsWith("/api/posts", StringComparison.OrdinalIgnoreCase))
            return true;
        if (p.StartsWith("/api/forums", StringComparison.OrdinalIgnoreCase))
            return true;
        if (p.StartsWith("/api/server", StringComparison.OrdinalIgnoreCase))
            return true;
        if (p.StartsWith("/api/media", StringComparison.OrdinalIgnoreCase))
            return true;
        if (p.StartsWith("/api/emotes", StringComparison.OrdinalIgnoreCase))
            return true;
        if (p.StartsWith("/api/federation/invites", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool IsMemberOnlyForumPath(string path) =>
        path.Equals("/api/forums/hub-memberships/sync", StringComparison.OrdinalIgnoreCase) ||
        EndsWithForumAction(path, "/join") ||
        EndsWithForumAction(path, "/leave") ||
        EndsWithForumAction(path, "/favorite");

    private static bool EndsWithForumAction(string path, string action) =>
        path.StartsWith("/api/forums/", StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(action, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path)
    {
        var trimmed = path.Trim();
        if (trimmed.Length == 0) return "/";
        if (!trimmed.StartsWith('/'))
            trimmed = "/" + trimmed;
        var withoutSlash = trimmed.TrimEnd('/');
        return withoutSlash.Length == 0 ? "/" : withoutSlash;
    }
}
