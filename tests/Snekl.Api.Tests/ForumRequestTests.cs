using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Snekl.Domain.Dtos;
using Snekl.Domain.Enums;

namespace Snekl.Api.Tests;

[Collection(ContentApiCollection.Name)]
public class ForumRequestTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ContentApiFixture _fixture;

    public ForumRequestTests(ContentApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task RequestCreateForum_IsHiddenUntilApproved()
    {
        var requester = await RegisterAsync("req");
        await _fixture.GrantForumRequesterRoleAsync(requester.User.Id);
        using var requesterClient = Authed(requester.Token);

        var slug = $"ask-{Guid.NewGuid():N}"[..18];
        var create = await requesterClient.PostAsJsonAsync("/api/forums", new CreateForumRequest(
            "Please add this",
            slug,
            "A requested forum"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var pending = await create.Content.ReadFromJsonAsync<ForumDto>(JsonOptions);
        Assert.NotNull(pending);
        Assert.Equal(ForumApprovalStatuses.Pending, pending!.ApprovalStatus);

        var publicList = await _fixture.Client.GetFromJsonAsync<List<ForumDto>>("/api/forums", JsonOptions);
        Assert.DoesNotContain(publicList ?? [], f => f.Id == pending.Id);

        var requesterList = await requesterClient.GetFromJsonAsync<List<ForumDto>>("/api/forums", JsonOptions);
        Assert.Contains(requesterList ?? [], f => f.Id == pending.Id);

        var thread = await requesterClient.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            pending.Id,
            null,
            "Hello",
            "Title"));
        Assert.Equal(HttpStatusCode.BadRequest, thread.StatusCode);

        var approver = await RegisterAsync("apr");
        await _fixture.GrantMemberRoleAsync(approver.User.Id);
        using var approverClient = Authed(approver.Token);

        var approve = await approverClient.PostAsync($"/api/forums/{slug}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var published = await approve.Content.ReadFromJsonAsync<ForumDto>(JsonOptions);
        Assert.Equal(ForumApprovalStatuses.Approved, published!.ApprovalStatus);

        var after = await _fixture.Client.GetFromJsonAsync<List<ForumDto>>("/api/forums", JsonOptions);
        Assert.Contains(after ?? [], f => f.Id == pending.Id);
    }

    [Fact]
    public async Task RequesterCannotApproveOwnForum()
    {
        var requester = await RegisterAsync("own");
        await _fixture.GrantForumRequesterRoleAsync(requester.User.Id);
        using var requesterClient = Authed(requester.Token);

        var slug = $"own-{Guid.NewGuid():N}"[..18];
        var create = await requesterClient.PostAsJsonAsync("/api/forums", new CreateForumRequest(
            "Own request",
            slug,
            "Should stay pending"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var approve = await requesterClient.PostAsync($"/api/forums/{slug}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
    }

    private async Task<AuthResponse> RegisterAsync(string prefix)
    {
        var username = $"{prefix}_{Guid.NewGuid():N}"[..12];
        var register = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username,
            $"{username}@example.com",
            "Password1!",
            "Test User"));
        register.EnsureSuccessStatusCode();
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        Assert.NotNull(auth);
        return auth!;
    }

    private HttpClient Authed(string token)
    {
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
