using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Snekl.Domain.Dtos;

namespace Snekl.Api.Tests;

[Collection(ContentApiCollection.Name)]
public class ForumJoinTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ContentApiFixture _fixture;

    public ForumJoinTests(ContentApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SecondUser_CanJoinAndLeave_IdempotentJoin()
    {
        var owner = await RegisterAsync("own");
        await _fixture.GrantMemberRoleAsync(owner.User.Id);
        using var ownerClient = Authed(owner.Token);

        var slug = $"club-{Guid.NewGuid():N}"[..18];
        var create = await ownerClient.PostAsJsonAsync("/api/forums", new CreateForumRequest(
            "Club",
            slug,
            "A joinable forum"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var forum = await create.Content.ReadFromJsonAsync<ForumDto>(JsonOptions);
        Assert.NotNull(forum);
        Assert.True(forum!.ViewerIsMember);

        var member = await RegisterAsync("mem");
        using var memberClient = Authed(member.Token);

        var before = await memberClient.GetFromJsonAsync<ForumDto>($"/api/forums/{slug}", JsonOptions);
        Assert.NotNull(before);
        Assert.False(before!.ViewerIsMember);

        var join = await memberClient.PostAsync($"/api/forums/{slug}/join", null);
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
        var joined = await join.Content.ReadFromJsonAsync<ForumDto>(JsonOptions);
        Assert.True(joined!.ViewerIsMember);

        var again = await memberClient.PostAsync($"/api/forums/{slug}/join", null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var still = await again.Content.ReadFromJsonAsync<ForumDto>(JsonOptions);
        Assert.True(still!.ViewerIsMember);

        var leave = await memberClient.PostAsync($"/api/forums/{slug}/leave", null);
        Assert.Equal(HttpStatusCode.OK, leave.StatusCode);
        var left = await leave.Content.ReadFromJsonAsync<ForumDto>(JsonOptions);
        Assert.False(left!.ViewerIsMember);
        Assert.False(left.ViewerIsFavorite);
    }

    [Fact]
    public async Task Favorite_RequiresMembership_AndClearsOnLeave()
    {
        var owner = await RegisterAsync("fav");
        await _fixture.GrantMemberRoleAsync(owner.User.Id);
        using var ownerClient = Authed(owner.Token);

        var slug = $"star-{Guid.NewGuid():N}"[..18];
        (await ownerClient.PostAsJsonAsync("/api/forums", new CreateForumRequest(
            "Star",
            slug,
            "Favorite me"))).EnsureSuccessStatusCode();

        var member = await RegisterAsync("fst");
        using var memberClient = Authed(member.Token);

        var refused = await memberClient.PutAsJsonAsync($"/api/forums/{slug}/favorite", new SetForumFavoriteRequest(true));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        (await memberClient.PostAsync($"/api/forums/{slug}/join", null)).EnsureSuccessStatusCode();
        var starred = await memberClient.PutAsJsonAsync($"/api/forums/{slug}/favorite", new SetForumFavoriteRequest(true));
        Assert.Equal(HttpStatusCode.OK, starred.StatusCode);
        var favorite = await starred.Content.ReadFromJsonAsync<ForumDto>(JsonOptions);
        Assert.True(favorite!.ViewerIsMember);
        Assert.True(favorite.ViewerIsFavorite);

        var listed = await memberClient.GetFromJsonAsync<List<ForumDto>>("/api/forums", JsonOptions);
        Assert.Contains(listed ?? [], f => f.Slug == slug && f.ViewerIsFavorite);

        (await memberClient.PostAsync($"/api/forums/{slug}/leave", null)).EnsureSuccessStatusCode();
        var afterLeave = await memberClient.GetFromJsonAsync<ForumDto>($"/api/forums/{slug}", JsonOptions);
        Assert.False(afterLeave!.ViewerIsMember);
        Assert.False(afterLeave.ViewerIsFavorite);
    }

    [Fact]
    public async Task LastOwner_CannotLeave()
    {
        var owner = await RegisterAsync("lst");
        await _fixture.GrantMemberRoleAsync(owner.User.Id);
        using var ownerClient = Authed(owner.Token);

        var slug = $"keep-{Guid.NewGuid():N}"[..18];
        var create = await ownerClient.PostAsJsonAsync("/api/forums", new CreateForumRequest(
            "Keep",
            slug,
            "Owner stays"));
        create.EnsureSuccessStatusCode();

        var leave = await ownerClient.PostAsync($"/api/forums/{slug}/leave", null);
        Assert.Equal(HttpStatusCode.BadRequest, leave.StatusCode);
        var body = await leave.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions);
        Assert.Contains("last owner", body?.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BannedUser_CannotJoin()
    {
        var owner = await RegisterAsync("ban");
        await _fixture.GrantMemberRoleAsync(owner.User.Id);
        using var ownerClient = Authed(owner.Token);

        var slug = $"gate-{Guid.NewGuid():N}"[..18];
        (await ownerClient.PostAsJsonAsync("/api/forums", new CreateForumRequest(
            "Gate",
            slug,
            "Banned out"))).EnsureSuccessStatusCode();

        var target = await RegisterAsync("tgt");
        using var targetClient = Authed(target.Token);

        var ban = await ownerClient.PostAsJsonAsync($"/api/forums/{slug}/bans", new BanForumMemberRequest(
            target.User.Username,
            "nope",
            null));
        Assert.Equal(HttpStatusCode.NoContent, ban.StatusCode);

        var join = await targetClient.PostAsync($"/api/forums/{slug}/join", null);
        Assert.Equal(HttpStatusCode.BadRequest, join.StatusCode);
    }

    [Fact]
    public async Task Creator_IsMemberWithoutJoin()
    {
        var owner = await RegisterAsync("crt");
        await _fixture.GrantMemberRoleAsync(owner.User.Id);
        using var ownerClient = Authed(owner.Token);

        var slug = $"made-{Guid.NewGuid():N}"[..18];
        var create = await ownerClient.PostAsJsonAsync("/api/forums", new CreateForumRequest(
            "Made",
            slug,
            "Already in"));
        var forum = await create.Content.ReadFromJsonAsync<ForumDto>(JsonOptions);
        Assert.True(forum!.ViewerIsMember);
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

    private sealed record ErrorBody(string Error);
}
