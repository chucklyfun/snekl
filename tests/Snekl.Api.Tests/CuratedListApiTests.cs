using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Snekl.Domain.Dtos;

namespace Snekl.Api.Tests;

[Collection(ContentApiCollection.Name)]
public class CuratedListApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ContentApiFixture _fixture;

    public CuratedListApiTests(ContentApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreateSubscribeAnnotate_OnlySubscribersSeeBadges()
    {
        var owner = await RegisterAsync("own");
        var target = await RegisterAsync("tgt");
        var subscriber = await RegisterAsync("sub");
        var stranger = await RegisterAsync("str");
        await _fixture.GrantMemberRoleAsync(owner.User.Id);
        await _fixture.GrantMemberRoleAsync(target.User.Id);

        using var ownerClient = Authed(owner.Token);
        using var subClient = Authed(subscriber.Token);
        using var strangerClient = Authed(stranger.Token);

        var created = await ownerClient.PostAsJsonAsync("/api/lists", new CreateCuratedListRequest(
            "Verified folks",
            "People we trust",
            "Verify",
            "✅",
            null,
            true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var list = await created.Content.ReadFromJsonAsync<CuratedListDto>(JsonOptions);
        Assert.NotNull(list);
        Assert.True(list!.IsOwner);

        var invalid = await ownerClient.PostAsJsonAsync("/api/lists", new CreateCuratedListRequest(
            "Nope", "", "banana", "x", null, true));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var entry = await ownerClient.PostAsJsonAsync($"/api/lists/{list.Id}/entries", new AddListEntryRequest(
            target.User.Id,
            "Verified",
            "Known in this scene",
            "✅",
            null,
            null,
            null,
            null));
        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);

        var nonOwnerEntry = await subClient.PostAsJsonAsync($"/api/lists/{list.Id}/entries", new AddListEntryRequest(
            subscriber.User.Id, "Nope", "tip", null, null, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, nonOwnerEntry.StatusCode);

        var forum = await ownerClient.PostAsJsonAsync("/api/forums", new CreateForumRequest(
            "List Forum",
            $"lf-{Guid.NewGuid():N}"[..16],
            "annos"));
        forum.EnsureSuccessStatusCode();
        var forumDto = await forum.Content.ReadFromJsonAsync<ForumDto>(JsonOptions);

        using var targetClient = Authed(target.Token);
        var join = await targetClient.PostAsync($"/api/forums/{forumDto!.Slug}/join", null);
        join.EnsureSuccessStatusCode();
        var post = await targetClient.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            forumDto!.Id,
            null,
            "Hello from a listed person",
            "Listed"));
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var createdPost = await post.Content.ReadFromJsonAsync<PostDto>(JsonOptions);
        Assert.NotNull(createdPost);

        var before = await strangerClient.GetFromJsonAsync<PostDto>($"/api/posts/{createdPost!.Id}", JsonOptions);
        Assert.Empty(before!.Author.Annotations);

        var subscribe = await subClient.PostAsJsonAsync(
            $"/api/lists/{list.Id}/subscribe",
            new SubscribeListRequest("Highlight"));
        Assert.Equal(HttpStatusCode.NoContent, subscribe.StatusCode);

        var after = await subClient.GetFromJsonAsync<PostDto>($"/api/posts/{createdPost.Id}", JsonOptions);
        var note = Assert.Single(after!.Author.Annotations);
        Assert.Equal("Verified", note.Label);
        Assert.Equal("Known in this scene", note.TooltipText);

        var unsub = await subClient.DeleteAsync($"/api/lists/{list.Id}/subscribe");
        Assert.Equal(HttpStatusCode.NoContent, unsub.StatusCode);
        var again = await subClient.DeleteAsync($"/api/lists/{list.Id}/subscribe");
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    private async Task<AuthResponse> RegisterAsync(string prefix)
    {
        var username = $"{prefix}_{Guid.NewGuid():N}"[..12];
        var register = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username,
            $"{username}@example.com",
            "Password1!",
            "List User"));
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
