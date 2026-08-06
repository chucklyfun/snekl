using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
}
