using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Snekl.Account.Domain.Dtos;
using Snekl.Tests.Shared;

namespace Snekl.Account.Api.Tests;

[Collection(AccountApiCollection.Name)]
public class AuthWorkflowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AccountApiFixture _fixture;

    public AuthWorkflowTests(AccountApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Reauthenticate_IssuesTokenWithAuthTime()
    {
        var username = $"re_{Guid.NewGuid():N}"[..16];
        var email = $"{username}@example.com";
        var register = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username, email, "Password1!", "Reauth User"));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var session = await register.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);
        Assert.NotNull(session);

        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var bad = await client.PostAsJsonAsync("/api/auth/reauthenticate", new ReauthenticateRequest("wrong-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);

        var ok = await client.PostAsJsonAsync("/api/auth/reauthenticate", new ReauthenticateRequest("Password1!"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var refreshed = await ok.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);
        Assert.NotNull(refreshed);
        Assert.False(string.IsNullOrWhiteSpace(refreshed!.AccessToken));

        var parts = refreshed.AccessToken.Split('.');
        Assert.Equal(3, parts.Length);
        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var payload = JsonDocument.Parse(payloadJson);
        Assert.True(payload.RootElement.TryGetProperty("auth_time", out var authTime));
        Assert.True(long.TryParse(authTime.ToString(), out var seconds));
        Assert.InRange(seconds, DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(), DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds());
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }

    [Fact]
    public async Task RegisterWithDidProof_ThenLogin_ThenMe()
    {
        var username = $"user_{Guid.NewGuid():N}"[..16];
        var email = $"{username}@example.com";
        var (keys, proof) = DidProofHelper.CreateRegistrationProof(username);

        var registerResponse = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username,
            email,
            "Password1!",
            "Test User",
            keys.Did,
            proof));

        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        var session = await registerResponse.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);
        Assert.NotNull(session);
        Assert.False(string.IsNullOrWhiteSpace(session!.AccessToken));
        Assert.Equal(keys.Did, session.Person.Did);
        Assert.Equal(username, session.Person.CanonicalUsername);
        Assert.True(session.Person.EmailVerified);

        var loginResponse = await _fixture.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Password1!"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginSession = await loginResponse.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);
        Assert.NotNull(loginSession);
        Assert.Equal(keys.Did, loginSession!.Person.Did);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginSession.AccessToken);
        var meResponse = await _fixture.Client.SendAsync(meRequest);
        var meBody = await meResponse.Content.ReadAsStringAsync();
        Assert.True(meResponse.IsSuccessStatusCode, $"GET /api/me failed: {(int)meResponse.StatusCode} {meBody}");
        var me = JsonSerializer.Deserialize<PersonClusterDto>(meBody, JsonOptions);
        Assert.NotNull(me);
        Assert.Equal(email, me!.Email);
    }

    [Fact]
    public async Task Register_WithoutDid_GeneratesKeyMaterial()
    {
        var username = $"gen_{Guid.NewGuid():N}"[..16];
        var response = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username,
            $"{username}@example.com",
            "Password1!",
            "Generated"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);
        Assert.NotNull(session);
        Assert.StartsWith("did:key:", session!.Person.Did);
        Assert.False(string.IsNullOrWhiteSpace(session.GeneratedKeyPrivatePkcs8Base64));
    }

    [Fact]
    public async Task Register_RejectsInvalidOwnershipProof()
    {
        var username = $"bad_{Guid.NewGuid():N}"[..16];
        var (keys, _) = DidProofHelper.CreateRegistrationProof(username);

        var response = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username,
            $"{username}@example.com",
            "Password1!",
            "Bad Proof",
            keys.Did,
            "z" + new string('1', 64)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Jwks_ReturnsOctetKey()
    {
        var response = await _fixture.Client.GetAsync("/.well-known/jwks.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jwks = await response.Content.ReadFromJsonAsync<JwksResponse>(JsonOptions);
        Assert.NotNull(jwks);
        Assert.NotEmpty(jwks!.Keys);
        Assert.Equal("oct", jwks.Keys[0].Kty);
        Assert.Equal("HS256", jwks.Keys[0].Alg);
        Assert.False(string.IsNullOrWhiteSpace(jwks.Keys[0].N));
    }

    [Fact]
    public async Task DevResetPassword_AllowsLoginWithNewPassword()
    {
        var username = $"rst_{Guid.NewGuid():N}"[..16];
        var email = $"{username}@example.com";
        var register = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username, email, "OldPassword1!", "Reset User"));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var reset = await _fixture.Client.PostAsJsonAsync("/api/auth/dev/reset-password",
            new DevResetPasswordRequest(email, "NewPassword1!"));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var loginOld = await _fixture.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "OldPassword1!"));
        Assert.Equal(HttpStatusCode.Unauthorized, loginOld.StatusCode);

        var loginNew = await _fixture.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "NewPassword1!"));
        Assert.Equal(HttpStatusCode.OK, loginNew.StatusCode);
    }

    [Fact]
    public async Task AuthServer_ReturnsPublicIdentity()
    {
        var response = await _fixture.Client.GetAsync("/api/auth/server");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<AuthServerInfoDto>(JsonOptions);
        Assert.NotNull(info);
        Assert.Equal("Snekl Auth Test", info.Name);
        Assert.Equal(_fixture.AuthBaseUrl, info.BaseUrl);
        Assert.Contains("/.well-known/jwks.json", info.JwksUrl);
    }

    [Fact]
    public async Task AdminBootstrap_ReturnsAuthKind()
    {
        var response = await _fixture.Client.GetAsync("/admin/bootstrap");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bootstrap = await response.Content.ReadFromJsonAsync<AdminBootstrapDto>(JsonOptions);
        Assert.NotNull(bootstrap);
        Assert.Equal("auth", bootstrap.Kind);
        Assert.Equal(_fixture.AuthBaseUrl, bootstrap.AuthLoginUrl);
    }

    [Fact]
    public async Task Federations_UnauthorizedWithoutToken()
    {
        var response = await _fixture.Client.GetAsync("/api/me/federations");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ForumFavorites_RoundTrip()
    {
        var username = $"fav_{Guid.NewGuid():N}"[..16];
        var email = $"{username}@example.com";
        var register = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username, email, "Password1!", "Fav User"));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var session = await register.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);
        Assert.NotNull(session);

        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var empty = await client.GetFromJsonAsync<List<ForumFavoriteDto>>("/api/me/forum-favorites", JsonOptions);
        Assert.NotNull(empty);
        Assert.Empty(empty);

        var created = await client.PostAsJsonAsync("/api/me/forum-favorites", new UpsertForumFavoriteRequest(
            "did:web:localhost%3A5001",
            Guid.NewGuid().ToString(),
            "Garden",
            "garden",
            null,
            "Snekl Alpha"));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var favorite = await created.Content.ReadFromJsonAsync<ForumFavoriteDto>(JsonOptions);
        Assert.NotNull(favorite);
        Assert.Equal("Garden", favorite!.Name);
        Assert.Equal("garden", favorite.Slug);

        var listed = await client.GetFromJsonAsync<List<ForumFavoriteDto>>("/api/me/forum-favorites", JsonOptions);
        Assert.NotNull(listed);
        Assert.Single(listed!);
        Assert.Equal(favorite.ForumId, listed[0].ForumId);

        var rename = await client.PostAsJsonAsync("/api/me/forum-favorites", new UpsertForumFavoriteRequest(
            favorite.ServerDid,
            favorite.ForumId,
            "Garden Club",
            "garden",
            null,
            "Snekl Alpha"));
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

        var afterRename = await client.GetFromJsonAsync<List<ForumFavoriteDto>>("/api/me/forum-favorites", JsonOptions);
        Assert.NotNull(afterRename);
        Assert.Single(afterRename);
        Assert.Equal("Garden Club", afterRename[0].Name);

        var delete = await client.DeleteAsync(
            $"/api/me/forum-favorites?serverDid={Uri.EscapeDataString(favorite.ServerDid)}&forumId={Uri.EscapeDataString(favorite.ForumId)}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var afterDelete = await client.GetFromJsonAsync<List<ForumFavoriteDto>>("/api/me/forum-favorites", JsonOptions);
        Assert.NotNull(afterDelete);
        Assert.Empty(afterDelete);
    }

    [Fact]
    public async Task Follows_RoundTrip()
    {
        var username = $"fol_{Guid.NewGuid():N}"[..16];
        var email = $"{username}@example.com";
        var register = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username, email, "Password1!", "Follow User"));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var session = await register.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);
        Assert.NotNull(session);

        var targetName = $"tgt_{Guid.NewGuid():N}"[..16];
        var targetRegister = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            targetName, $"{targetName}@example.com", "Password1!", "Ada"));
        Assert.Equal(HttpStatusCode.OK, targetRegister.StatusCode);
        var target = await targetRegister.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);
        Assert.NotNull(target);
        var targetDid = target!.Person.Did;

        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var empty = await client.GetFromJsonAsync<List<FollowDto>>("/api/me/follows", JsonOptions);
        Assert.NotNull(empty);
        Assert.Empty(empty);

        var created = await client.PostAsJsonAsync("/api/me/follows", new UpsertFollowRequest(
            targetDid, "Ada", targetName));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var follow = await created.Content.ReadFromJsonAsync<FollowDto>(JsonOptions);
        Assert.NotNull(follow);
        Assert.Equal(targetDid, follow!.Did);
        Assert.Equal("Ada", follow.DisplayName);
        Assert.Equal(targetName, follow.Username);

        var listed = await client.GetFromJsonAsync<List<FollowDto>>("/api/me/follows", JsonOptions);
        Assert.NotNull(listed);
        Assert.Single(listed!);
        Assert.Equal(targetDid, listed[0].Did);

        var rename = await client.PostAsJsonAsync("/api/me/follows", new UpsertFollowRequest(
            targetDid, "Ada Lovelace", targetName));
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

        var afterRename = await client.GetFromJsonAsync<List<FollowDto>>("/api/me/follows", JsonOptions);
        Assert.NotNull(afterRename);
        Assert.Single(afterRename);
        Assert.Equal("Ada Lovelace", afterRename[0].DisplayName);

        var self = await client.PostAsJsonAsync("/api/me/follows", new UpsertFollowRequest(session.Person.Did));
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);

        var invalid = await client.PostAsJsonAsync("/api/me/follows", new UpsertFollowRequest("not-a-did"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var delete = await client.DeleteAsync(
            $"/api/me/follows?did={Uri.EscapeDataString(targetDid)}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var afterDelete = await client.GetFromJsonAsync<List<FollowDto>>("/api/me/follows", JsonOptions);
        Assert.NotNull(afterDelete);
        Assert.Empty(afterDelete);
    }

    [Fact]
    public async Task Follows_Contains_IsTrueOnlyAfterFollow()
    {
        var followerName = $"fc_{Guid.NewGuid():N}"[..16];
        var followerRegister = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            followerName, $"{followerName}@example.com", "Password1!", "Follower"));
        Assert.Equal(HttpStatusCode.OK, followerRegister.StatusCode);
        var follower = await followerRegister.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);
        Assert.NotNull(follower);

        var targetName = $"tg_{Guid.NewGuid():N}"[..16];
        var targetRegister = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            targetName, $"{targetName}@example.com", "Password1!", "Target"));
        Assert.Equal(HttpStatusCode.OK, targetRegister.StatusCode);
        var target = await targetRegister.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);
        Assert.NotNull(target);

        var before = await _fixture.Client.GetFromJsonAsync<FollowContainsDto>(
            $"/api/follows/contains?followerDid={Uri.EscapeDataString(follower!.Person.Did)}&targetDid={Uri.EscapeDataString(target!.Person.Did)}",
            JsonOptions);
        Assert.NotNull(before);
        Assert.False(before.Follows);

        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", follower.AccessToken);
        var created = await client.PostAsJsonAsync("/api/me/follows", new UpsertFollowRequest(target.Person.Did));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var after = await _fixture.Client.GetFromJsonAsync<FollowContainsDto>(
            $"/api/follows/contains?followerDid={Uri.EscapeDataString(follower.Person.Did)}&targetDid={Uri.EscapeDataString(target.Person.Did)}",
            JsonOptions);
        Assert.NotNull(after);
        Assert.True(after.Follows);
    }

    [Fact]
    public async Task Follows_Contains_MissingArgsUnknownAndUnfollow()
    {
        var missing = await _fixture.Client.GetAsync("/api/follows/contains");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var unknown = await _fixture.Client.GetFromJsonAsync<FollowContainsDto>(
            "/api/follows/contains?followerDid=did:key:unknown1&targetDid=did:key:unknown2",
            JsonOptions);
        Assert.NotNull(unknown);
        Assert.False(unknown.Follows);

        var followerName = $"fu_{Guid.NewGuid():N}"[..16];
        var followerRegister = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            followerName, $"{followerName}@example.com", "Password1!", "Follower"));
        followerRegister.EnsureSuccessStatusCode();
        var follower = await followerRegister.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);
        var targetName = $"tu_{Guid.NewGuid():N}"[..16];
        var targetRegister = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            targetName, $"{targetName}@example.com", "Password1!", "Target"));
        targetRegister.EnsureSuccessStatusCode();
        var target = await targetRegister.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);

        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", follower!.AccessToken);
        (await client.PostAsJsonAsync("/api/me/follows", new UpsertFollowRequest(target!.Person.Did))).EnsureSuccessStatusCode();
        var delete = await client.DeleteAsync(
            $"/api/me/follows?did={Uri.EscapeDataString(target.Person.Did)}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var afterUnfollow = await _fixture.Client.GetFromJsonAsync<FollowContainsDto>(
            $"/api/follows/contains?followerDid={Uri.EscapeDataString(follower.Person.Did)}&targetDid={Uri.EscapeDataString(target.Person.Did)}",
            JsonOptions);
        Assert.False(afterUnfollow!.Follows);
    }

    [Fact]
    public async Task ForumGroups_RoundTripNestedFolders()
    {
        var username = $"grp_{Guid.NewGuid():N}"[..16];
        var email = $"{username}@example.com";
        var register = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username, email, "Password1!", "Group User"));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var session = await register.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);
        Assert.NotNull(session);

        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var empty = await client.GetFromJsonAsync<ForumNavLayoutDto>("/api/me/forum-groups", JsonOptions);
        Assert.NotNull(empty);
        Assert.Empty(empty.Folders);

        var artId = Guid.NewGuid().ToString();
        var fanId = Guid.NewGuid().ToString();
        var saved = await client.PutAsJsonAsync("/api/me/forum-groups", new ForumNavLayoutDto(
            [
                new ForumNavFolderDto(artId, "Art", null, 0),
                new ForumNavFolderDto(fanId, "Fanart", artId, 0)
            ],
            [
                new ForumNavFolderItemDto(
                    fanId,
                    "did:web:localhost%3A5001",
                    Guid.NewGuid().ToString(),
                    "Roses",
                    "roses",
                    null,
                    "Snekl Alpha",
                    0)
            ],
            [
                new ForumNavHiddenDto("section:joined", "did:web:localhost%3A5001", Guid.NewGuid().ToString())
            ]));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var layout = await saved.Content.ReadFromJsonAsync<ForumNavLayoutDto>(JsonOptions);
        Assert.NotNull(layout);
        Assert.Equal(2, layout!.Folders.Count);
        Assert.Single(layout.Items);
        Assert.NotNull(layout.Hidden);
        Assert.Single(layout.Hidden);
        Assert.Equal("section:joined", layout.Hidden[0].SectionId);
        Assert.Equal("Fanart", layout.Folders.First(f => f.Id == fanId).Name);

        var cleared = await client.PutAsJsonAsync("/api/me/forum-groups", new ForumNavLayoutDto([], [], []));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var after = await client.GetFromJsonAsync<ForumNavLayoutDto>("/api/me/forum-groups", JsonOptions);
        Assert.NotNull(after);
        Assert.Empty(after.Folders);
        Assert.Empty(after.Items);
        Assert.Empty(after.Hidden ?? []);
    }

    [Fact]
    public async Task FirstOperator_CanClaimAuthServer()
    {
        var username = $"op_{Guid.NewGuid():N}"[..16];
        var email = $"{username}@example.com";
        var register = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username, email, "Password1!", "Operator"));
        register.EnsureSuccessStatusCode();
        var session = await register.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);
        Assert.NotNull(session);

        using var member = _fixture.CreateClient();
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var status = await member.GetFromJsonAsync<AuthOperatorStatusDto>("/api/auth/admin/status", JsonOptions);
        Assert.NotNull(status);
        Assert.Contains(status.State, ["unclaimed", "forbidden", "ready"]);
        if (status.State != "unclaimed")
            return;

        var login = await member.PostAsJsonAsync(
            "/api/auth/admin/login",
            new AuthOperatorLoginRequest(NewAdminPassword: "Operator1!"));
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<AuthAdminAuthResponse>(JsonOptions);
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));
    }

    [Fact]
    public async Task CopiedBearerToken_IsRejected()
    {
        var username = $"lock_{Guid.NewGuid():N}"[..16];
        var register = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username, $"{username}@example.com", "Password1!", "Locked"));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var session = await register.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);
        Assert.NotNull(session);

        using var copied = _fixture.Factory.CreateClient();
        copied.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);
        var me = await copied.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        using var browser = _fixture.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var ok = await browser.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }
}
