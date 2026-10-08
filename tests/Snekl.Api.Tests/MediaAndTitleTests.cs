using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Snekl.Domain.Dtos;

namespace Snekl.Api.Tests;

[Collection(ContentApiCollection.Name)]
public class MediaAndTitleTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Valid 1x1 RGBA PNG
    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private readonly ContentApiFixture _fixture;

    public MediaAndTitleTests(ContentApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreateThread_RequiresTitle()
    {
        var authed = await CreateAuthedMemberAsync();
        var forum = await CreateForumAsync(authed);

        var response = await authed.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            forum.Id,
            null,
            "Body without title"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadMedia_RejectsNonImage()
    {
        var authed = await CreateAuthedMemberAsync();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("not an image"))
        {
            Headers = { ContentType = new MediaTypeHeaderValue("text/plain") }
        }, "file", "notes.txt");

        var response = await authed.PostAsync("/api/media", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadMedia_AndCreateThreadWithAttachment_ThenTopReplyPreview()
    {
        var authed = await CreateAuthedMemberAsync();
        var forum = await CreateForumAsync(authed);

        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new ByteArrayContent(TinyPng)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("image/png") }
        }, "file", "pixel.png");

        var uploadResponse = await authed.PostAsync("/api/media", uploadContent);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        var attachment = await uploadResponse.Content.ReadFromJsonAsync<MediaAttachmentDto>(JsonOptions);
        Assert.NotNull(attachment);
        Assert.Contains("/api/media/", attachment!.Url);

        var getMedia = await _fixture.Client.GetAsync($"/api/media/{attachment.Id}");
        Assert.Equal(HttpStatusCode.OK, getMedia.StatusCode);
        Assert.Equal("image/png", getMedia.Content.Headers.ContentType?.MediaType);

        var postResponse = await authed.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            forum.Id,
            null,
            "Opening post with image",
            "Thread with media",
            [attachment.Id]));
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var root = await postResponse.Content.ReadFromJsonAsync<PostDto>(JsonOptions);
        Assert.NotNull(root);
        Assert.Equal("Thread with media", root!.Title);
        Assert.NotNull(root.Attachments);
        Assert.Single(root.Attachments!);
        Assert.Equal(attachment.Id, root.Attachments[0].Id);

        var replyResponse = await authed.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            forum.Id,
            root.Id,
            "This is the top reply candidate with enough text to preview."));
        Assert.Equal(HttpStatusCode.Created, replyResponse.StatusCode);

        var listResponse = await authed.GetAsync($"/api/posts/forum/{forum.Id}?sort=new");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var threads = await listResponse.Content.ReadFromJsonAsync<List<PostDto>>(JsonOptions);
        Assert.NotNull(threads);
        var listed = Assert.Single(threads!);
        Assert.Equal(root.Id, listed.Id);
        Assert.Equal("Thread with media", listed.Title);
        Assert.NotNull(listed.TopReplyPreview);
        Assert.Contains("top reply candidate", listed.TopReplyPreview!.Message);
        Assert.Empty(listed.Replies);
    }

    [Fact]
    public async Task NsfwAttachment_GatesOriginal_AndServesPreview()
    {
        var authed = await CreateAuthedMemberAsync();
        var forum = await CreateForumAsync(authed);

        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new ByteArrayContent(TinyPng)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("image/png") }
        }, "file", "pixel.png");
        var uploadResponse = await authed.PostAsync("/api/media", uploadContent);
        var attachment = await uploadResponse.Content.ReadFromJsonAsync<MediaAttachmentDto>(JsonOptions);
        Assert.NotNull(attachment);

        var missingCover = await authed.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            forum.Id,
            null,
            "NSFW without cover",
            "NSFW missing cover",
            [attachment!.Id],
            NsfwMedia: [new NsfwMediaHintDto(attachment.Id, "  ")]));
        Assert.Equal(HttpStatusCode.BadRequest, missingCover.StatusCode);

        var postResponse = await authed.PostAsJsonAsync("/api/posts", new CreatePostRequest(
            forum.Id,
            null,
            "NSFW with cover",
            "NSFW gated image",
            [attachment.Id],
            NsfwMedia: [new NsfwMediaHintDto(attachment.Id, "A covered beach photo.")]));
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var root = await postResponse.Content.ReadFromJsonAsync<PostDto>(JsonOptions);
        Assert.NotNull(root);
        var att = Assert.Single(root!.Attachments!);
        Assert.True(att.IsNsfw);
        Assert.Equal("A covered beach photo.", att.CoverText);
        Assert.NotNull(att.Url);
        Assert.Contains("/preview", att.CensorUrl);

        var anonymousOriginal = await _fixture.Client.GetAsync($"/api/media/{attachment.Id}");
        Assert.Equal(HttpStatusCode.NotFound, anonymousOriginal.StatusCode);

        var anonymousPreview = await _fixture.Client.GetAsync($"/api/media/{attachment.Id}/preview");
        Assert.Equal(HttpStatusCode.OK, anonymousPreview.StatusCode);
        Assert.Equal("image/jpeg", anonymousPreview.Content.Headers.ContentType?.MediaType);

        var authorOriginal = await authed.GetAsync($"/api/media/{attachment.Id}");
        Assert.Equal(HttpStatusCode.OK, authorOriginal.StatusCode);

        var other = await CreateAuthedMemberAsync();
        var otherOriginal = await other.GetAsync($"/api/media/{attachment.Id}");
        Assert.Equal(HttpStatusCode.NotFound, otherOriginal.StatusCode);

        var listed = await other.GetFromJsonAsync<PostDto>($"/api/posts/{root.Id}", JsonOptions);
        Assert.NotNull(listed);
        var listedAtt = Assert.Single(listed!.Attachments!);
        Assert.True(listedAtt.IsNsfw);
        Assert.Null(listedAtt.Url);
        Assert.NotNull(listedAtt.CensorUrl);

        var updated = await authed.PatchAsJsonAsync(
            $"/api/posts/{root.Id}/media/{attachment.Id}/nsfw",
            new UpdatePostMediaNsfwRequest(true, "Updated cover of a beach."));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var afterEdit = await updated.Content.ReadFromJsonAsync<PostDto>(JsonOptions);
        Assert.Equal("Updated cover of a beach.", Assert.Single(afterEdit!.Attachments!).CoverText);

        var denied = await other.PatchAsJsonAsync(
            $"/api/posts/{root.Id}/media/{attachment.Id}/nsfw",
            new UpdatePostMediaNsfwRequest(true, "Someone else's cover."));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    private async Task<HttpClient> CreateAuthedMemberAsync()
    {
        var username = $"m_{Guid.NewGuid():N}"[..12];
        var register = await _fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            username,
            $"{username}@example.com",
            "Password1!",
            "Media User"));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        Assert.NotNull(auth);
        await _fixture.GrantMemberRoleAsync(auth!.User.Id);

        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }

    private static async Task<ForumDto> CreateForumAsync(HttpClient authed)
    {
        var forumResponse = await authed.PostAsJsonAsync("/api/forums", new CreateForumRequest(
            "Media Forum",
            $"media-{Guid.NewGuid():N}"[..18],
            "Forum for media tests"));
        Assert.Equal(HttpStatusCode.Created, forumResponse.StatusCode);
        var forum = await forumResponse.Content.ReadFromJsonAsync<ForumDto>(JsonOptions);
        Assert.NotNull(forum);
        return forum!;
    }
}
