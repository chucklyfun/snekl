using System.Security.Claims;
using Snekl.Domain;
using Snekl.Infrastructure.Security;

namespace Snekl.Infrastructure.Tests;

public class RecentPasswordAuthTests
{
    [Fact]
    public void HasRecentPasswordAuth_RequiresFreshAuthTimeClaim()
    {
        var fresh = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(OAuthClaims.AuthTime, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        ]));
        Assert.True(RecentPasswordAuth.HasRecentPasswordAuth(fresh, TimeSpan.FromMinutes(5)));

        var stale = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(OAuthClaims.AuthTime, DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString())
        ]));
        Assert.False(RecentPasswordAuth.HasRecentPasswordAuth(stale, TimeSpan.FromMinutes(5)));

        var missing = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.False(RecentPasswordAuth.HasRecentPasswordAuth(missing, TimeSpan.FromMinutes(5)));
    }
}
