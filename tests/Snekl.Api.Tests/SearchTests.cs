using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Snekl.Domain.Dtos;

namespace Snekl.Api.Tests;

[Collection(ContentApiCollection.Name)]
public class SearchTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ContentApiFixture _fixture;

    public SearchTests(ContentApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Search_FindsThreadAndReply_HidesUnrelated()
    {
        var username = $"s_{Guid.NewGuid():N}"[..12];
        var register = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username,
            $"{username}@example.com",
            "Password1!",
            "Search User"));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        Assert.NotNull(auth);
        await _fixture.GrantMemberRoleAsync(auth!.User.Id);

        using var authed = _fixture.Factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var forumResponse = await authed.PostAsJsonAsync("/api/forums", new CreateForumRequest(
            "Search lab",
            $"search-{Guid.NewGuid():N}"[..18],
            "Searchable forum"));
        Assert.Equal(HttpStatusCode.Created, forumResponse.StatusCode);
        var forum = await forumResponse.Content.ReadFromJsonAsync<ForumDto>(JsonOptions);
        Assert.NotNull(forum);

        var hit = await authed.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            forum!.Id,
            null,
            "A thread about pineapple pizza",
            "Dinner debate"));
        Assert.Equal(HttpStatusCode.Created, hit.StatusCode);
        var hitPost = await hit.Content.ReadFromJsonAsync<PostDto>(JsonOptions);
        Assert.NotNull(hitPost);

        var miss = await authed.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            forum.Id,
            null,
            "Completely unrelated gardening notes",
            "Tomatoes"));
        Assert.Equal(HttpStatusCode.Created, miss.StatusCode);

        var reply = await authed.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            forum.Id,
            hitPost!.Id,
            "Also, anchovy pineapple is a crime."));
        Assert.Equal(HttpStatusCode.Created, reply.StatusCode);

        var titleHits = await authed.GetFromJsonAsync<List<SearchHitDto>>(
            "/api/search?q=Dinner",
            JsonOptions);
        Assert.NotNull(titleHits);
        Assert.Contains(titleHits, h => h.Thread.Post.Id == hitPost.Id);
        Assert.DoesNotContain(titleHits, h => h.Thread.Post.Title == "Tomatoes");

        var replyHits = await authed.GetFromJsonAsync<List<SearchHitDto>>(
            "/api/search?q=anchovy",
            JsonOptions);
        Assert.NotNull(replyHits);
        var replyHit = Assert.Single(replyHits, h => h.Thread.Post.Id == hitPost.Id);
        Assert.True(replyHit.MatchedInReply);
        Assert.Contains("anchovy", replyHit.Snippet, StringComparison.OrdinalIgnoreCase);

        var empty = await authed.GetFromJsonAsync<List<SearchHitDto>>("/api/search?q=", JsonOptions);
        Assert.NotNull(empty);
        Assert.Empty(empty);
    }
}
