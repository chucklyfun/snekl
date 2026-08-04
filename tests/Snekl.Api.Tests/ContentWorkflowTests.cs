using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Snekl.Domain.Dtos;

namespace Snekl.Api.Tests;

[Collection(ContentApiCollection.Name)]
public class ContentWorkflowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ContentApiFixture _fixture;

    public ContentWorkflowTests(ContentApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Register_CreateForum_Post_Reply_Upvote()
    {
        var username = $"c_{Guid.NewGuid():N}"[..12];
        var register = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username,
            $"{username}@example.com",
            "Password1!",
            "Content User"));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        Assert.NotNull(auth);

        await _fixture.GrantMemberRoleAsync(auth!.User.Id);

        using var authed = CreateAuthedClient(auth.Token);

        var forumResponse = await authed.PostAsJsonAsync("/api/forums", new CreateForumRequest(
            "General",
            $"general-{Guid.NewGuid():N}"[..18],
            "A test forum"));
        Assert.Equal(HttpStatusCode.Created, forumResponse.StatusCode);
        var forum = await forumResponse.Content.ReadFromJsonAsync<ForumDto>(JsonOptions);
        Assert.NotNull(forum);

        var postResponse = await authed.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            forum!.Id,
            null,
            "Hello thread"));
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var post = await postResponse.Content.ReadFromJsonAsync<PostDto>(JsonOptions);
        Assert.NotNull(post);
        Assert.Equal("Hello thread", post!.Message);

        var replyResponse = await authed.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            forum.Id,
            post.Id,
            "A reply"));
        Assert.Equal(HttpStatusCode.Created, replyResponse.StatusCode);

        var upvoteResponse = await authed.PostAsync($"/api/posts/{post.Id}/upvote", null);
        Assert.Equal(HttpStatusCode.OK, upvoteResponse.StatusCode);
        var upvote = await upvoteResponse.Content.ReadFromJsonAsync<UpvoteResultDto>(JsonOptions);
        Assert.NotNull(upvote);
        Assert.True(upvote!.ViewerHasUpvoted);
        Assert.Equal(1, upvote.UpvoteCount);

        var threadResponse = await authed.GetAsync($"/api/posts/{post.Id}");
        Assert.Equal(HttpStatusCode.OK, threadResponse.StatusCode);
        var thread = await threadResponse.Content.ReadFromJsonAsync<PostDto>(JsonOptions);
        Assert.NotNull(thread);
        Assert.Equal(1, thread!.ReplyCount);
    }

    [Fact]
    public async Task SiteAdmin_Login_And_GetNode()
    {
        var login = await _fixture.Client.PostAsJsonAsync("/api/admin/login", new LoginRequest("admin", "admin"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var adminAuth = await login.Content.ReadFromJsonAsync<SiteAdminAuthResponse>(JsonOptions);
        Assert.NotNull(adminAuth);
        Assert.False(string.IsNullOrWhiteSpace(adminAuth!.Token));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/site/node");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth.Token);
        var nodeResponse = await _fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, nodeResponse.StatusCode);
    }

    [Fact]
    public async Task PublicNodeEndpoint_ReturnsLocalNode()
    {
        var response = await _fixture.Client.GetAsync("/api/node");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var node = await response.Content.ReadFromJsonAsync<NodeInfoDto>(JsonOptions);
        Assert.NotNull(node);
        Assert.Equal("http://content.test", node!.BaseUrl);
    }

    private HttpClient CreateAuthedClient(string token)
    {
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
