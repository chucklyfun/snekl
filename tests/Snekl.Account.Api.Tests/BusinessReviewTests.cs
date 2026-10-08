using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Security;

namespace Snekl.Account.Api.Tests;

[Collection(AccountApiCollection.Name)]
public class BusinessReviewTests
{
    // Must match Auth:AdminSigningKey in AccountApiFixture.
    private const string AdminSigningKey = "snekl-auth-admin-test-signing-key-min-32!!";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AccountApiFixture _fixture;

    public BusinessReviewTests(AccountApiFixture fixture) => _fixture = fixture;

    /// <summary>
    /// An auth-operator token shaped like AuthOperatorService issues. Minted directly so these tests
    /// don't depend on which test in the shared database claimed the server first.
    /// </summary>
    private HttpClient OperatorClient(string username = "op-tester")
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AdminSigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _fixture.AuthBaseUrl,
            audience: AuthAdminAuth.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim("did", "did:key:zOperatorForTests"),
                new Claim("username", username),
                new Claim(AuthAdminAuth.RoleClaim, AuthAdminAuth.Role)
            ],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: creds);

        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    private async Task<(BusinessAccountDto Business, BusinessSignupRequest Request)> SignUpAsync()
    {
        var username = $"rv_{Guid.NewGuid():N}"[..16];
        var request = new BusinessSignupRequest(
            BusinessName: "Review Co",
            ContactName: "Riley Review",
            Email: $"{username}@example.com",
            Username: username,
            Password: "Password1!",
            Services: [BusinessServiceKeys.ServerHosting]);
        var response = await _fixture.Client.PostAsJsonAsync("/api/business/signup", request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        var business = JsonSerializer.Deserialize<BusinessAccountDto>(body, JsonOptions)!;
        Assert.Equal(BusinessAccountStatuses.Pending, business.Status);
        return (business, request);
    }

    private async Task<HttpClient> MemberClientAsync(BusinessSignupRequest request)
    {
        var login = await _fixture.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(request.Email, request.Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = await login.Content.ReadFromJsonAsync<RingSessionDto>(JsonOptions);
        var member = _fixture.CreateClient();
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);
        return member;
    }

    private async Task<BusinessAccountDto> MineAsync(HttpClient member, Guid businessId)
    {
        var response = await member.GetAsync("/api/business/me");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        var mine = JsonSerializer.Deserialize<List<BusinessAccountDto>>(body, JsonOptions)!;
        return Assert.Single(mine, b => b.Id == businessId);
    }

    [Fact]
    public async Task Approve_MarksApproved_RecordsReviewer_AndOwnerSeesIt()
    {
        var (business, request) = await SignUpAsync();
        using var op = OperatorClient("op-approver");

        var response = await op.PostAsync($"/api/business/accounts/{business.Id}/approve", null);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        var approved = JsonSerializer.Deserialize<BusinessAccountDto>(body, JsonOptions)!;
        Assert.Equal(BusinessAccountStatuses.Approved, approved.Status);
        Assert.Equal("op-approver", approved.ReviewedBy);
        Assert.NotNull(approved.ReviewedAt);
        Assert.Null(approved.ServerId);
        Assert.Equal(business.PersonClusterId, approved.PersonClusterId);

        // Approval does not create a server; the owner sees the approval and can create one later.
        using var member = await MemberClientAsync(request);
        var mine = await MineAsync(member, business.Id);
        Assert.Equal(BusinessAccountStatuses.Approved, mine.Status);
        Assert.Null(mine.ServerId);

        var list = await op.GetFromJsonAsync<List<BusinessAccountDto>>("/api/business/accounts", JsonOptions);
        Assert.Equal(BusinessAccountStatuses.Approved, Assert.Single(list!, b => b.Id == business.Id).Status);
    }

    [Fact]
    public async Task Approve_Twice_IsIdempotent()
    {
        var (business, _) = await SignUpAsync();
        using var first = OperatorClient("op-first");
        using var second = OperatorClient("op-second");

        var one = await first.PostAsync($"/api/business/accounts/{business.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        var afterFirst = await one.Content.ReadFromJsonAsync<BusinessAccountDto>(JsonOptions);

        var two = await second.PostAsync($"/api/business/accounts/{business.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, two.StatusCode);
        var afterSecond = await two.Content.ReadFromJsonAsync<BusinessAccountDto>(JsonOptions);

        Assert.Equal(BusinessAccountStatuses.Approved, afterSecond!.Status);
        // The second call changes nothing: the original reviewer and time stay.
        Assert.Equal("op-first", afterSecond.ReviewedBy);
        Assert.Equal(afterFirst!.ReviewedAt, afterSecond.ReviewedAt);
    }

    [Fact]
    public async Task Approve_RejectedAccount_IsConflict_AndStaysRejected()
    {
        var (business, _) = await SignUpAsync();
        using var op = OperatorClient();

        var reject = await op.PostAsync($"/api/business/accounts/{business.Id}/reject", null);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal(BusinessAccountStatuses.Rejected,
            (await reject.Content.ReadFromJsonAsync<BusinessAccountDto>(JsonOptions))!.Status);

        var approve = await op.PostAsync($"/api/business/accounts/{business.Id}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        Assert.Contains("Only pending", await approve.Content.ReadAsStringAsync());

        var rejectAgain = await op.PostAsync($"/api/business/accounts/{business.Id}/reject", null);
        Assert.Equal(HttpStatusCode.OK, rejectAgain.StatusCode);

        var list = await op.GetFromJsonAsync<List<BusinessAccountDto>>("/api/business/accounts", JsonOptions);
        Assert.Equal(BusinessAccountStatuses.Rejected, Assert.Single(list!, b => b.Id == business.Id).Status);
    }

    [Fact]
    public async Task Reject_ApprovedAccount_IsConflict()
    {
        var (business, _) = await SignUpAsync();
        using var op = OperatorClient();

        Assert.Equal(HttpStatusCode.OK, (await op.PostAsync($"/api/business/accounts/{business.Id}/approve", null)).StatusCode);
        var reject = await op.PostAsync($"/api/business/accounts/{business.Id}/reject", null);
        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
    }

    [Fact]
    public async Task Approve_UnknownAccount_IsNotFound()
    {
        using var op = OperatorClient();
        var response = await op.PostAsync($"/api/business/accounts/{Guid.NewGuid()}/approve", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ApproveAndReject_RejectNonOperators()
    {
        var (business, request) = await SignUpAsync();

        foreach (var action in new[] { "approve", "reject" })
        {
            var anonymous = await _fixture.Client.PostAsync($"/api/business/accounts/{business.Id}/{action}", null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        // The business owner's own member token is not an operator token.
        using var member = await MemberClientAsync(request);
        foreach (var action in new[] { "approve", "reject" })
        {
            var asMember = await member.PostAsync($"/api/business/accounts/{business.Id}/{action}", null);
            Assert.Equal(HttpStatusCode.Unauthorized, asMember.StatusCode);
        }

        Assert.Equal(BusinessAccountStatuses.Pending, (await MineAsync(member, business.Id)).Status);
    }

    [Fact]
    public async Task Mine_RequiresSignIn()
    {
        var response = await _fixture.Client.GetAsync("/api/business/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
