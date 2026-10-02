using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Snekl.Tests.Shared;
using AccountDtos = Snekl.Account.Domain.Dtos;
using ContentDtos = Snekl.Domain.Dtos;

namespace Snekl.Api.Tests;

[Collection(DualApiCollection.Name)]
public class AuthToContentReplyPolicyTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DualApiFixture _fixture;

    public AuthToContentReplyPolicyTests(DualApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FollowingOnly_NestedPutLockModeratorWatercoolerAndQuotes()
    {
        var (opSession, opMe) = await RegisterContentUserAsync();
        var (friendSession, friendMe) = await RegisterContentUserAsync();
        var (strangerSession, strangerMe) = await RegisterContentUserAsync();
        var (modSession, modMe) = await RegisterContentUserAsync();

        using var authOp = _fixture.CreateAuthClient();
        authOp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opSession.AccessToken);
        (await authOp.PostAsJsonAsync("/api/me/follows", new AccountDtos.UpsertFollowRequest(friendSession.Person.Did)))
            .EnsureSuccessStatusCode();

        using var op = AuthedContent(opSession.AccessToken);
        using var friend = AuthedContent(friendSession.AccessToken);
        using var stranger = AuthedContent(strangerSession.AccessToken);
        using var mod = AuthedContent(modSession.AccessToken);

        var forumResponse = await op.PostAsJsonAsync("/api/forums", new ContentDtos.CreateForumRequest(
            "Policy Forum",
            $"pf-{Guid.NewGuid():N}"[..16],
            "Reply policy extras"));
        forumResponse.EnsureSuccessStatusCode();
        var forum = await forumResponse.Content.ReadFromJsonAsync<ContentDtos.ForumDto>(JsonOptions);

        var invalid = await op.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            forum!.Id, null, "Bad policy", "Bad", ReplyPolicy: "friends"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var defaultPost = await op.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            forum.Id, null, "Open", "Open"));
        defaultPost.EnsureSuccessStatusCode();
        var open = await defaultPost.Content.ReadFromJsonAsync<ContentDtos.PostDto>(JsonOptions);
        Assert.Equal("everyone", open!.ReplyPolicy);
        var strangerOnOpen = await stranger.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            forum.Id, open.Id, "anyone"));
        Assert.Equal(HttpStatusCode.Created, strangerOnOpen.StatusCode);

        var postResponse = await op.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            forum.Id, null, "Closed", "Closed", ReplyPolicy: "following"));
        postResponse.EnsureSuccessStatusCode();
        var post = await postResponse.Content.ReadFromJsonAsync<ContentDtos.PostDto>(JsonOptions);

        var friendReply = await friend.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            forum.Id, post!.Id, "nested parent"));
        friendReply.EnsureSuccessStatusCode();
        var friendReplyDto = await friendReply.Content.ReadFromJsonAsync<ContentDtos.PostDto>(JsonOptions);

        var nestedStranger = await stranger.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            forum.Id, friendReplyDto!.Id, "nested stranger"));
        Assert.Equal(HttpStatusCode.BadRequest, nestedStranger.StatusCode);

        var strangerPut = await stranger.PutAsJsonAsync(
            $"/api/posts/{post.Id}/reply-policy",
            new ContentDtos.UpdateReplyPolicyRequest("everyone"));
        Assert.Equal(HttpStatusCode.Forbidden, strangerPut.StatusCode);

        var replyPut = await op.PutAsJsonAsync(
            $"/api/posts/{friendReplyDto.Id}/reply-policy",
            new ContentDtos.UpdateReplyPolicyRequest("everyone"));
        Assert.Equal(HttpStatusCode.BadRequest, replyPut.StatusCode);

        var openPolicy = await op.PutAsJsonAsync(
            $"/api/posts/{post.Id}/reply-policy",
            new ContentDtos.UpdateReplyPolicyRequest("everyone"));
        openPolicy.EnsureSuccessStatusCode();
        var strangerNow = await stranger.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            forum.Id, post.Id, "now allowed"));
        Assert.Equal(HttpStatusCode.Created, strangerNow.StatusCode);

        (await op.PutAsJsonAsync(
            $"/api/posts/{post.Id}/reply-policy",
            new ContentDtos.UpdateReplyPolicyRequest("following"))).EnsureSuccessStatusCode();

        await JoinAsync(mod, forum.Slug);
        var roles = await op.GetFromJsonAsync<List<ContentDtos.ForumRoleDto>>(
            $"/api/forums/{forum.Slug}/roles", JsonOptions);
        var moderator = Assert.Single(roles!, r => r.Name == "Moderator");
        var assign = await op.PutAsJsonAsync(
            $"/api/forums/{forum.Slug}/members/{modMe.User.Id}/roles",
            new ContentDtos.AssignForumRolesRequest([moderator.Id]));
        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);

        var modReply = await mod.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            forum.Id, post.Id, "mod bypass"));
        Assert.Equal(HttpStatusCode.Created, modReply.StatusCode);

        var lockResponse = await op.PutAsync($"/api/posts/{post.Id}/lock?locked=true", null);
        lockResponse.EnsureSuccessStatusCode();
        var friendLocked = await friend.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            forum.Id, post.Id, "followed but locked"));
        Assert.Equal(HttpStatusCode.BadRequest, friendLocked.StatusCode);
        var lockedThread = await friend.GetFromJsonAsync<ContentDtos.PostDto>($"/api/posts/{post.Id}", JsonOptions);
        Assert.False(lockedThread!.ViewerActions?.CanReply);

        var cooler = await op.PostAsJsonAsync("/api/watercoolers", new ContentDtos.CreateWatercoolerRequest(
            "Policy cooler",
            $"pc-{Guid.NewGuid():N}"[..12],
            "chat",
            forum.Id,
            false,
            true));
        cooler.EnsureSuccessStatusCode();
        var coolerDto = await cooler.Content.ReadFromJsonAsync<ContentDtos.WatercoolerDto>(JsonOptions);
        Assert.Equal("approved", coolerDto!.ApprovalStatus);

        var coolerRoot = await op.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            null, null, "cooler closed", WatercoolerId: coolerDto.Id, ReplyPolicy: "following"));
        coolerRoot.EnsureSuccessStatusCode();
        var coolerPost = await coolerRoot.Content.ReadFromJsonAsync<ContentDtos.PostDto>(JsonOptions);

        var coolerFriend = await friend.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            null, coolerPost!.Id, "cooler friend", WatercoolerId: coolerDto.Id));
        Assert.Equal(HttpStatusCode.Created, coolerFriend.StatusCode);
        var coolerStranger = await stranger.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            null, coolerPost.Id, "cooler stranger", WatercoolerId: coolerDto.Id));
        Assert.Equal(HttpStatusCode.BadRequest, coolerStranger.StatusCode);

        var quote = await stranger.PostAsJsonAsync("/api/posts", new ContentDtos.CreatePostRequest(
            null, null, "quoting", WatercoolerId: coolerDto.Id, QuotedPostId: coolerPost.Id));
        Assert.Equal(HttpStatusCode.Created, quote.StatusCode);
        var quoteDto = await quote.Content.ReadFromJsonAsync<ContentDtos.PostDto>(JsonOptions);
        Assert.Equal(coolerPost.Id, quoteDto!.QuotedPostId);
        Assert.Equal("everyone", quoteDto.ReplyPolicy);
    }

    private async Task<(AccountDtos.RingSessionDto Session, ContentDtos.AuthMeResponse Me)> RegisterContentUserAsync()
    {
        var username = $"p_{Guid.NewGuid():N}"[..12];
        var email = $"{username}@example.com";
        var (keys, proof) = DidProofHelper.CreateRegistrationProof(username);
        var register = await _fixture.AuthClient.PostAsJsonAsync("/api/auth/register", new AccountDtos.RegisterRequest(
            username, email, "Password1!", "Policy User", keys.Did, proof));
        register.EnsureSuccessStatusCode();
        var session = await register.Content.ReadFromJsonAsync<AccountDtos.RingSessionDto>(JsonOptions);
        using var content = AuthedContent(session!.AccessToken);
        var me = await content.GetFromJsonAsync<ContentDtos.AuthMeResponse>("/api/auth/me", JsonOptions);
        await _fixture.GrantMemberRoleAsync(me!.User.Id);
        return (session, me);
    }

    private HttpClient AuthedContent(string token)
    {
        var client = _fixture.CreateContentClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task JoinAsync(HttpClient client, string slug)
    {
        var join = await client.PostAsync($"/api/forums/{slug}/join", null);
        join.EnsureSuccessStatusCode();
    }
}
