using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Services;

namespace Snekl.Account.Api.Tests;

[Collection(AccountApiCollection.Name)]
public class BusinessSignupTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AccountApiFixture _fixture;

    public BusinessSignupTests(AccountApiFixture fixture) => _fixture = fixture;

    private static BusinessSignupRequest NewRequest(string? username = null, IReadOnlyList<string>? services = null) =>
        NewRequestFor(username ?? $"biz_{Guid.NewGuid():N}"[..16], services);

    private static BusinessSignupRequest NewRequestFor(string username, IReadOnlyList<string>? services) =>
        new(
            BusinessName: "Acme Widgets",
            ContactName: "Pat Example",
            Email: $"{username}@example.com",
            Username: username,
            Password: "Password1!",
            Services: services ?? [BusinessServiceKeys.ServerHosting],
            Website: "https://acme.example");

    [Fact]
    public async Task SignUp_CreatesPendingBusiness_AndAccountCanLogIn()
    {
        var request = NewRequest(services: [BusinessServiceKeys.ServerHosting, BusinessServiceKeys.Other]) with
        {
            OtherServices = "Custom emotes for our brand"
        };

        var response = await _fixture.Client.PostAsJsonAsync("/api/business/signup", request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);

        var business = JsonSerializer.Deserialize<BusinessAccountDto>(body, JsonOptions);
        Assert.NotNull(business);
        Assert.Equal("Acme Widgets", business!.BusinessName);
        Assert.Equal("Pat Example", business.ContactName);
        Assert.Equal(request.Email, business.ContactEmail);
        Assert.Equal(request.Username, business.Username);
        Assert.Equal(BusinessAccountStatuses.Pending, business.Status);
        Assert.Equal([BusinessServiceKeys.ServerHosting, BusinessServiceKeys.Other], business.Services);
        Assert.Equal("Custom emotes for our brand", business.OtherServices);
        Assert.Equal("https://acme.example", business.Website);

        // The contact is a normal Snekl Account: email + password login works.
        var login = await _fixture.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(request.Email, request.Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = await login.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);
        Assert.Equal(business.PersonClusterId, session!.Person.Id);
        Assert.Equal("Pat Example", session.Person.DisplayName);
    }

    [Fact]
    public async Task SignUp_RejectsDuplicateEmail_WithoutCreatingSecondAccount()
    {
        var first = NewRequest();
        var ok = await _fixture.Client.PostAsJsonAsync("/api/business/signup", first);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var second = NewRequest() with { Email = first.Email };
        var dup = await _fixture.Client.PostAsJsonAsync("/api/business/signup", second);
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        var error = await dup.Content.ReadAsStringAsync();
        Assert.Contains("already exists", error);

        // The rejected username must still be free (nothing was half-created).
        var retry = await _fixture.Client.PostAsJsonAsync("/api/business/signup",
            second with { Email = $"{second.Username}@example.com" });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Theory]
    [InlineData("", "Pat", "good@example.com", "goodname", "Password1!", "server-hosting", null, "Business name")]
    [InlineData("Acme", "", "good@example.com", "goodname", "Password1!", "server-hosting", null, "Contact name")]
    [InlineData("Acme", "Pat", "not-an-email", "goodname", "Password1!", "server-hosting", null, "valid email")]
    [InlineData("Acme", "Pat", "good@example.com", "No Spaces", "Password1!", "server-hosting", null, "Username")]
    [InlineData("Acme", "Pat", "good@example.com", "goodname", "short", "server-hosting", null, "Password")]
    [InlineData("Acme", "Pat", "good@example.com", "goodname", "Password1!", "", null, "at least one service")]
    [InlineData("Acme", "Pat", "good@example.com", "goodname", "Password1!", "teleportation", null, "Unknown service")]
    [InlineData("Acme", "Pat", "good@example.com", "goodname", "Password1!", "other", null, "other service")]
    public async Task SignUp_ValidatesInput(
        string businessName,
        string contactName,
        string email,
        string username,
        string password,
        string service,
        string? other,
        string expectedError)
    {
        var request = new BusinessSignupRequest(
            businessName,
            contactName,
            email,
            username,
            password,
            string.IsNullOrEmpty(service) ? [] : [service],
            other);

        var response = await _fixture.Client.PostAsJsonAsync("/api/business/signup", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedError, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignUp_RequiresDeviceKey()
    {
        using var bare = _fixture.Factory.CreateClient();
        var response = await bare.PostAsJsonAsync("/api/business/signup", NewRequest());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListAccounts_RequiresOperatorToken()
    {
        var anonymous = await _fixture.Client.GetAsync("/api/business/accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var username = $"bm_{Guid.NewGuid():N}"[..16];
        var register = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username, $"{username}@example.com", "Password1!", "Member"));
        var session = await register.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);

        using var member = _fixture.CreateClient();
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);
        var asMember = await member.GetAsync("/api/business/accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, asMember.StatusCode);
    }

    [Fact]
    public void Validate_NormalizesInput()
    {
        var result = BusinessAccountService.Validate(new BusinessSignupRequest(
            "  Acme  ",
            " Pat ",
            " Pat@Example.COM ",
            " Acme_Co ",
            "Password1!",
            ["SERVER-HOSTING", "server-hosting", " support "],
            OtherServices: "ignored when other is not chosen",
            Website: " ",
            Message: "  hi  "));

        Assert.Equal("Acme", result.BusinessName);
        Assert.Equal("Pat", result.ContactName);
        Assert.Equal("pat@example.com", result.Email);
        Assert.Equal("acme_co", result.Username);
        Assert.Equal(["server-hosting", "support"], result.Services);
        Assert.Null(result.OtherServices);
        Assert.Null(result.Website);
        Assert.Equal("hi", result.Message);
    }
}
