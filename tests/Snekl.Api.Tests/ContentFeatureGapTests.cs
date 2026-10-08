using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Snekl.Domain.Dtos;

namespace Snekl.Api.Tests;

[Collection(ContentApiCollection.Name)]
public class ContentFeatureGapTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ContentApiFixture _fixture;

    public ContentFeatureGapTests(ContentApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ForumHistory_DeniedForMembers_RoleUpdatePersists()
    {
        var owner = await RegisterMemberAsync("own");
        using var ownerClient = Authed(owner.Token);

        var slug = $"hist-{Guid.NewGuid():N}"[..16];
        var forumRes = await ownerClient.PostAsJsonAsync("/api/forums", new CreateForumRequest("History", slug, "log"));
        forumRes.EnsureSuccessStatusCode();

        var roles = await ownerClient.GetFromJsonAsync<List<ForumRoleDto>>($"/api/forums/{slug}/roles", JsonOptions);
        var memberRole = Assert.Single(roles!, r => r.Name == "Member");
        var update = await ownerClient.PutAsJsonAsync(
            $"/api/forums/{slug}/roles/{memberRole.Id}",
            new UpdateForumRoleRequest("Member", "#99aab5", memberRole.Position, ["SeeForum", "ViewForum", "Reply"]));
        update.EnsureSuccessStatusCode();
        var updated = await update.Content.ReadFromJsonAsync<ForumRoleDto>(JsonOptions);
        Assert.Contains("Reply", updated!.Permissions);
        Assert.DoesNotContain("CreateThread", updated.Permissions);

        var ownerHistory = await ownerClient.GetAsync($"/api/forums/{slug}/history");
        Assert.Equal(HttpStatusCode.OK, ownerHistory.StatusCode);

        var member = await RegisterMemberAsync("mem");
        using var memberClient = Authed(member.Token);
        (await memberClient.PostAsync($"/api/forums/{slug}/join", null)).EnsureSuccessStatusCode();
        var denied = await memberClient.GetAsync($"/api/forums/{slug}/history");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task PostRevisions_UnicodeReaction_SearchPage_DmEligibility_PendingCooler_AgeStatus()
    {
        var owner = await RegisterMemberAsync("gap");
        using var client = Authed(owner.Token);

        var slug = $"gap-{Guid.NewGuid():N}"[..16];
        var forumRes = await client.PostAsJsonAsync("/api/forums", new CreateForumRequest("Gaps", slug, "more"));
        forumRes.EnsureSuccessStatusCode();
        var forum = await forumRes.Content.ReadFromJsonAsync<ForumDto>(JsonOptions);

        var postRes = await client.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            forum!.Id, null, "original body", "Revision thread"));
        postRes.EnsureSuccessStatusCode();
        var post = await postRes.Content.ReadFromJsonAsync<PostDto>(JsonOptions);

        (await client.PutAsJsonAsync($"/api/posts/{post!.Id}", new UpdatePostRequest("edited body"))).EnsureSuccessStatusCode();
        var revisions = await client.GetFromJsonAsync<List<PostRevisionDto>>($"/api/posts/{post.Id}/revisions", JsonOptions);
        Assert.Contains(revisions!, r => r.Message.Contains("original"));

        var reacted = await client.PostAsJsonAsync(
            $"/api/posts/{post.Id}/reactions",
            new ToggleReactionRequest(Emoji: "👍"));
        reacted.EnsureSuccessStatusCode();
        var chips = await reacted.Content.ReadFromJsonAsync<List<PostReactionDto>>(JsonOptions);
        Assert.Contains(chips!, c => c.Emoji == "👍" && c.ViewerReacted);

        for (var i = 0; i < 3; i++)
        {
            (await client.PostAsJsonAsync("/api/posts", new CreatePostRequest(
                forum.Id, null, $"page filler {i}", $"Filler {i}"))).EnsureSuccessStatusCode();
        }

        var page2 = await client.GetFromJsonAsync<List<SearchHitDto>>("/api/search?q=Filler&page=2&pageSize=1", JsonOptions);
        Assert.NotNull(page2);
        Assert.True(page2.Count <= 1);

        var dm = await client.GetFromJsonAsync<DirectMessageEligibilityDto>(
            $"/api/dm/eligibility/{owner.User.Username}", JsonOptions);
        Assert.NotNull(dm);
        Assert.False(dm.CanRequest);

        var requester = await RegisterAsync("req");
        await _fixture.GrantForumRequesterRoleAsync(requester.User.Id);
        using var reqClient = Authed(requester.Token);
        var pending = await reqClient.PostAsJsonAsync("/api/watercoolers", new CreateWatercoolerRequest(
            "Ask plaza",
            $"ap-{Guid.NewGuid():N}"[..12],
            "please"));
        pending.EnsureSuccessStatusCode();
        var cooler = await pending.Content.ReadFromJsonAsync<WatercoolerDto>(JsonOptions);
        Assert.Equal("pending", cooler!.ApprovalStatus);

        var age = await client.GetFromJsonAsync<NsfwAgeCheckStatusDto>("/api/me/nsfw-age-check", JsonOptions);
        Assert.NotNull(age);
    }

    private async Task<AuthResponse> RegisterMemberAsync(string prefix)
    {
        var auth = await RegisterAsync(prefix);
        await _fixture.GrantMemberRoleAsync(auth.User.Id);
        return auth;
    }

    private async Task<AuthResponse> RegisterAsync(string prefix)
    {
        var username = $"{prefix}_{Guid.NewGuid():N}"[..12];
        var register = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username, $"{username}@example.com", "Password1!", "Gap User"));
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
