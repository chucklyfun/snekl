using Microsoft.AspNetCore.Http;
using Snekl.Domain;

namespace Snekl.Infrastructure.Security;

public static class InstallTokenGuard
{
    public static bool IsBlocked(HttpContext context)
    {
        var tokenUse = context.User.FindFirst(OAuthClaims.TokenUse)?.Value;
        if (!string.Equals(tokenUse, OAuthClaims.TokenUseInstall, StringComparison.Ordinal))
            return false;

        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return false;

        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/api/extensions", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
