using System.Security.Claims;

namespace Snekl.Account.Infrastructure.Security;

public static class AccountClaimsExtensions
{
    public static bool TryGetClusterId(this ClaimsPrincipal user, out Guid clusterId)
    {
        clusterId = default;
        var claim = user.FindFirstValue("cluster_id");
        return !string.IsNullOrWhiteSpace(claim) && Guid.TryParse(claim, out clusterId);
    }
}
