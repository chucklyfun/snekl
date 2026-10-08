using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Snekl.Domain;

namespace Snekl.Infrastructure.Security;

public static class RecentPasswordAuth
{
    public const string ReauthRequiredCode = "reauth_required";
    public const string DefaultWindowSecondsKey = "Snekl:OwnershipReauthWindowSeconds";
    public const int DefaultWindowSeconds = 300;

    public static bool HasRecentPasswordAuth(ClaimsPrincipal user, TimeSpan maxAge)
    {
        var raw = user.FindFirstValue(OAuthClaims.AuthTime);
        if (string.IsNullOrWhiteSpace(raw) || !long.TryParse(raw, out var seconds))
            return false;

        var authTime = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return DateTimeOffset.UtcNow - authTime <= maxAge;
    }

    public static TimeSpan ResolveWindow(IConfiguration? config)
    {
        var seconds = config?.GetValue(DefaultWindowSecondsKey, DefaultWindowSeconds) ?? DefaultWindowSeconds;
        if (seconds < 30)
            seconds = 30;
        if (seconds > 3600)
            seconds = 3600;
        return TimeSpan.FromSeconds(seconds);
    }

    public static ActionResult? RejectIfStale(ControllerBase controller)
    {
        var config = controller.HttpContext.RequestServices.GetService<IConfiguration>();
        var window = ResolveWindow(config);
        if (HasRecentPasswordAuth(controller.User, window))
            return null;

        return new UnauthorizedObjectResult(new
        {
            error = "Reauthentication required. Confirm your password with the auth server before transferring ownership.",
            code = ReauthRequiredCode
        });
    }
}
