using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Snekl.Domain;
using Snekl.Infrastructure.Security;

namespace Snekl.Infrastructure.Tests;

public class BotCapabilityGuardTests
{
    [Theory]
    [InlineData("GET", "/api/lists", false)]
    [InlineData("POST", "/api/posts", false)]
    [InlineData("POST", "/api/forums", false)]
    [InlineData("POST", "/api/media", false)]
    [InlineData("POST", "/api/emotes", false)]
    [InlineData("POST", "/api/federation/invites", false)]
    [InlineData("PUT", "/api/server/members/x/roles", false)]
    [InlineData("POST", "/hubs/thread/negotiate", false)]
    [InlineData("POST", "/api/lists", true)]
    [InlineData("PUT", "/api/users/me/flair", true)]
    [InlineData("POST", "/api/forums/hub-memberships/sync", true)]
    [InlineData("POST", "/api/forums/garden/join", true)]
    [InlineData("POST", "/api/forums/garden/leave", true)]
    [InlineData("PUT", "/api/forums/garden/favorite", true)]
    [InlineData("POST", "/api/extensions/installs", true)]
    [InlineData("POST", "/api/admin/login", true)]
    [InlineData("POST", "/api/identity/map", true)]
    public void BotWrites_AreDeniedUnlessGrantBacked(string method, string path, bool blocked)
    {
        var context = CreateContext(method, path, bot: true);
        Assert.Equal(blocked, BotCapabilityGuard.IsBlocked(context));
    }

    [Fact]
    public void HumanWrites_AreNotBlocked()
    {
        var context = CreateContext("POST", "/api/lists", bot: false);
        Assert.False(BotCapabilityGuard.IsBlocked(context));
    }

    [Fact]
    public void InstallToken_IsNotTreatedAsBot()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/lists";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(OAuthClaims.TokenUse, OAuthClaims.TokenUseInstall)
        ], "test"));
        Assert.False(BotCapabilityGuard.IsBlocked(context));
    }

    private static DefaultHttpContext CreateContext(string method, string path, bool bot)
    {
        var claims = new List<Claim>
        {
            new(OAuthClaims.TokenUse, bot ? OAuthClaims.TokenUseBot : OAuthClaims.TokenUseUser)
        };
        if (bot)
            claims.Add(new Claim(OAuthClaims.Bot, "true"));

        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return context;
    }
}
