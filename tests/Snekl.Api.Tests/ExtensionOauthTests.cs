using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Snekl.Tests.Shared;
using AccountDtos = Snekl.Account.Domain.Dtos;
using ContentDtos = Snekl.Domain.Dtos;

namespace Snekl.Api.Tests;

[Collection(DualApiCollection.Name)]
public class ExtensionOauthTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly DualApiFixture _fixture;

    public ExtensionOauthTests(DualApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AdminInstall_CreatesBotsThatPost_InstallTokenCannotPost()
    {
        var (userToken, userId) = await RegisterUserAsync("oa");
        await _fixture.GrantSuperUserAsync(userId);

        using var auth = _fixture.CreateAuthClient();
        auth.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        var appRes = await auth.PostAsJsonAsync("/api/oauth/apps", new AccountDtos.RegisterOAuthAppRequest(
            "Test Ext",
            ["https://ext.test/callback"]));
        Assert.Equal(HttpStatusCode.OK, appRes.StatusCode);
        var app = await appRes.Content.ReadFromJsonAsync<AccountDtos.RegisterOAuthAppResponse>(JsonOptions);
        Assert.NotNull(app);

        using var content = _fixture.CreateContentClient();
        content.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        var installRes = await content.PostAsJsonAsync("/api/extensions/installs", new ContentDtos.CreateExtensionInstallRequest(
            app!.ClientId,
            app.Name,
            null,
            null,
            "server",
            null,
            ["ViewServer", "CreateForum"],
            ["SeeForum", "ViewForum", "CreateThread", "Reply"]));
        var installBody = await installRes.Content.ReadAsStringAsync();
        Assert.True(installRes.IsSuccessStatusCode, installBody);
        var install = JsonSerializer.Deserialize<ContentDtos.ExtensionInstallDto>(installBody, JsonOptions);
        Assert.NotNull(install);
        Assert.NotNull(install!.RateLimiting);
        Assert.Equal("ip", install.RateLimiting!.Partition);

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var authorize = await auth.PostAsJsonAsync("/api/oauth/authorize", new AccountDtos.AuthorizeInstallRequest(
            app.ClientId,
            "https://ext.test/callback",
            S256(verifier),
            "S256",
            DualApiFixture.ContentBaseUrl,
            install!.Id,
            "xyz"));
        var authorizeBody = await authorize.Content.ReadAsStringAsync();
        Assert.True(authorize.IsSuccessStatusCode, authorizeBody);
        var redirect = JsonSerializer.Deserialize<AccountDtos.AuthorizeInstallResponse>(authorizeBody, JsonOptions);
        Assert.NotNull(redirect);
        var code = QueryValue(redirect!.RedirectTo, "code");
        Assert.False(string.IsNullOrWhiteSpace(code));

        var tokenRes = await _fixture.AuthClient.PostAsJsonAsync("/api/oauth/token", new AccountDtos.OAuthTokenRequest(
            "authorization_code",
            code,
            "https://ext.test/callback",
            app.ClientId,
            app.ClientSecret,
            verifier));
        var tokenBody = await tokenRes.Content.ReadAsStringAsync();
        Assert.True(tokenRes.IsSuccessStatusCode, tokenBody);
        var tokens = JsonSerializer.Deserialize<AccountDtos.OAuthTokenResponse>(tokenBody, JsonOptions);
        Assert.Equal("install", tokens!.TokenUse);
        Assert.NotNull(tokens.RateLimiting);
        Assert.Contains(
            tokens.RateLimiting!.Rules,
            r => r.PathPrefix == "/api/posts" && r.Method == "POST" && r.Limit == 30);

        using var asInstall = _fixture.CreateContentClient();
        asInstall.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var blocked = await asInstall.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            Guid.NewGuid(), null, "nope", "Nope"));
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        using var asApp = _fixture.CreateAuthClient();
        asApp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var botRes = await asApp.PostAsJsonAsync("/api/oauth/bots", new AccountDtos.CreateOAuthBotRequest(
            $"bot_{Guid.NewGuid():N}"[..12],
            "Mira Bot"));
        var botBody = await botRes.Content.ReadAsStringAsync();
        Assert.True(botRes.IsSuccessStatusCode, botBody);
        var bot = JsonSerializer.Deserialize<AccountDtos.OAuthBotCreatedResponse>(botBody, JsonOptions);
        Assert.NotNull(bot);

        using var asBot = _fixture.CreateContentClient();
        asBot.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bot!.AccessToken);
        var forumRes = await asBot.PostAsJsonAsync("/api/forums", new ContentDtos.CreateForumRequest(
            "Bot Forum",
            $"botf-{Guid.NewGuid():N}"[..12],
            "From a bot"));
        var forumBody = await forumRes.Content.ReadAsStringAsync();
        Assert.True(forumRes.IsSuccessStatusCode, forumBody);
        var forum = JsonSerializer.Deserialize<ContentDtos.ForumDto>(forumBody, JsonOptions);
        var postRes = await asBot.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            forum!.Id, null, "Hello from a bot", "Bot thread"));
        Assert.Equal(HttpStatusCode.Created, postRes.StatusCode);

        var lists = await asBot.PostAsJsonAsync("/api/lists", new ContentDtos.CreateCuratedListRequest(
            "Nope", "Bot list", "highlight", null, null, true));
        Assert.Equal(HttpStatusCode.Forbidden, lists.StatusCode);

        var flair = await asBot.PutAsJsonAsync("/api/users/me/flair", new ContentDtos.UpdateUserFlairRequest(
            "x", null, "nope", null, null));
        Assert.Equal(HttpStatusCode.Forbidden, flair.StatusCode);

        var sync = await asBot.PostAsync("/api/forums/hub-memberships/sync", null);
        Assert.Equal(HttpStatusCode.Forbidden, sync.StatusCode);

        var extraInstall = await asBot.PostAsJsonAsync("/api/extensions/installs", new ContentDtos.CreateExtensionInstallRequest(
            app.ClientId,
            app.Name,
            null,
            null,
            "server",
            null,
            ["ViewServer"],
            ["ViewForum"]));
        Assert.Equal(HttpStatusCode.Forbidden, extraInstall.StatusCode);

        var me = await asBot.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task InstallOptions_ReturnsExistingInstallForClient()
    {
        var (userToken, userId) = await RegisterUserAsync("ex");
        await _fixture.GrantSuperUserAsync(userId);

        using var auth = _fixture.CreateAuthClient();
        auth.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        var appRes = await auth.PostAsJsonAsync("/api/oauth/apps", new AccountDtos.RegisterOAuthAppRequest(
            "Reuse Ext",
            ["https://ext.test/callback"]));
        var app = await appRes.Content.ReadFromJsonAsync<AccountDtos.RegisterOAuthAppResponse>(JsonOptions);
        Assert.NotNull(app);

        using var content = _fixture.CreateContentClient();
        content.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        var installRes = await content.PostAsJsonAsync("/api/extensions/installs", new ContentDtos.CreateExtensionInstallRequest(
            app!.ClientId,
            app.Name,
            null,
            null,
            "server",
            null,
            ["ViewServer", "CreateForum"],
            ["SeeForum", "ViewForum", "CreateThread", "Reply"]));
        installRes.EnsureSuccessStatusCode();
        var install = await installRes.Content.ReadFromJsonAsync<ContentDtos.ExtensionInstallDto>(JsonOptions);

        var optionsRes = await content.GetAsync($"/api/extensions/install-options?clientId={Uri.EscapeDataString(app.ClientId)}");
        optionsRes.EnsureSuccessStatusCode();
        var options = await optionsRes.Content.ReadFromJsonAsync<ContentDtos.ExtensionInstallOptionsDto>(JsonOptions);
        Assert.NotNull(options);
        Assert.NotNull(options!.ExistingInstall);
        Assert.Equal(install!.Id, options.ExistingInstall!.Id);
        Assert.Equal("server", options.ExistingInstall.ResourceType);

        var again = await content.PostAsJsonAsync("/api/extensions/installs", new ContentDtos.CreateExtensionInstallRequest(
            app.ClientId,
            app.Name,
            null,
            null,
            "server",
            null,
            ["ViewServer", "CreateForum"],
            ["SeeForum", "ViewForum", "CreateThread", "Reply"]));
        again.EnsureSuccessStatusCode();
        var reused = await again.Content.ReadFromJsonAsync<ContentDtos.ExtensionInstallDto>(JsonOptions);
        Assert.Equal(install.Id, reused!.Id);

        var listRes = await content.GetAsync("/api/extensions");
        listRes.EnsureSuccessStatusCode();
        var listed = await listRes.Content.ReadFromJsonAsync<List<ContentDtos.ExtensionInstallDto>>(JsonOptions);
        Assert.NotNull(listed);
        Assert.Equal(1, listed!.Count(i => i.ClientId == app.ClientId));
    }

    private async Task<(string Token, Guid UserId)> RegisterUserAsync(string prefix)
    {
        var username = $"{prefix}_{Guid.NewGuid():N}"[..12];
        var (keys, proof) = DidProofHelper.CreateRegistrationProof(username);
        var register = await _fixture.AuthClient.PostAsJsonAsync("/api/auth/register", new AccountDtos.RegisterRequest(
            username,
            $"{username}@example.com",
            "Password1!",
            "Oauth User",
            keys.Did,
            proof));
        register.EnsureSuccessStatusCode();
        var session = await register.Content.ReadFromJsonAsync<AccountDtos.RingSessionDto>(JsonOptions);
        using var content = _fixture.CreateContentClient();
        content.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);
        var me = await content.GetFromJsonAsync<ContentDtos.AuthMeResponse>("/api/auth/me", JsonOptions);
        return (session.AccessToken, me!.User.Id);
    }

    private static string S256(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64Url(hash);
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? QueryValue(string url, string key)
    {
        var query = new Uri(url).Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var name = eq < 0 ? part : part[..eq];
            if (string.Equals(name, key, StringComparison.Ordinal))
                return eq < 0 ? "" : Uri.UnescapeDataString(part[(eq + 1)..]);
        }
        return null;
    }
}
