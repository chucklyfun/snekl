using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class EmoteServiceTests
{
    [Fact]
    public async Task CreateAndReact_UsesShortcodeCatalog()
    {
        await using var db = CreateDb();
        var user = new User
        {
            Username = "alice",
            DisplayName = "Alice",
            Email = "alice@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var forum = new Forum
        {
            Name = "General",
            Slug = "general",
            Description = "",
            CreatedByUserId = user.Id
        };
        db.Forums.Add(forum);
        await db.SaveChangesAsync();

        var asset = new MediaAsset
        {
            UploaderUserId = user.Id,
            StorageKey = "emotes/pog",
            ContentType = "image/png",
            ByteSize = 1200
        };
        db.MediaAssets.Add(asset);

        var post = new Post
        {
            ForumId = forum.Id,
            AuthorUserId = user.Id,
            Message = "hello :pog:"
        };
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var service = new EmoteService(db, new AllowAllPermissions(), new FakeMedia(), new NeverBanned(), new FakeNode());
        var emote = await service.CreateAsync(user.Id, new CreateEmoteRequest("Pog", asset.Id, forum.Id, "Pog"));
        Assert.Equal("pog", emote.Code);

        var reacted = await service.ToggleReactionAsync(
            post.Id, user.Id, new ToggleReactionRequest(emote.Id), CancellationToken.None);
        Assert.Single(reacted);
        Assert.True(reacted[0].ViewerReacted);
        Assert.Equal(1, reacted[0].Count);

        var removed = await service.ToggleReactionAsync(
            post.Id, user.Id, new ToggleReactionRequest(emote.Id), CancellationToken.None);
        Assert.Empty(removed);
    }

    [Fact]
    public async Task ToggleReaction_AcceptsUnicodeEmoji()
    {
        await using var db = CreateDb();
        var user = new User
        {
            Username = "bob",
            DisplayName = "Bob",
            Email = "bob@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var forum = new Forum { Name = "G", Slug = "g", Description = "", CreatedByUserId = user.Id };
        db.Forums.Add(forum);
        await db.SaveChangesAsync();
        var post = new Post { ForumId = forum.Id, AuthorUserId = user.Id, Message = "hi" };
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var service = new EmoteService(db, new AllowAllPermissions(), new FakeMedia(), new NeverBanned(), new FakeNode());
        var rows = await service.ToggleReactionAsync(
            post.Id, user.Id, new ToggleReactionRequest(Emoji: "👍"), CancellationToken.None);
        Assert.Single(rows);
        Assert.Equal("👍", rows[0].Emoji);
    }

    [Fact]
    public async Task UnicodeSkin_ReplacesChipImage()
    {
        await using var db = CreateDb();
        var user = new User
        {
            Username = "cara",
            DisplayName = "Cara",
            Email = "cara@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var forum = new Forum { Name = "G", Slug = "g", Description = "", CreatedByUserId = user.Id };
        db.Forums.Add(forum);
        await db.SaveChangesAsync();
        var asset = new MediaAsset
        {
            UploaderUserId = user.Id,
            StorageKey = "emotes/up",
            ContentType = "image/png",
            ByteSize = 800
        };
        db.MediaAssets.Add(asset);
        var post = new Post { ForumId = forum.Id, AuthorUserId = user.Id, Message = "hi" };
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var service = new EmoteService(db, new AllowAllPermissions(), new FakeMedia(), new NeverBanned(), new FakeNode());
        var emote = await service.CreateAsync(
            user.Id,
            new CreateEmoteRequest("updoot", asset.Id, forum.Id, "Updoot", "👍"));
        Assert.Equal("👍", emote.SkinsUnicode);

        var rows = await service.ToggleReactionAsync(
            post.Id, user.Id, new ToggleReactionRequest(Emoji: "👍"), CancellationToken.None);
        Assert.Single(rows);
        Assert.Equal("👍", rows[0].Emoji);
        Assert.Contains(asset.Id.ToString(), rows[0].ImageUrl);
    }

    [Fact]
    public async Task Update_ChangesCodeNameAndInlineEmoji()
    {
        await using var db = CreateDb();
        var user = new User
        {
            Username = "dana",
            DisplayName = "Dana",
            Email = "dana@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var forum = new Forum { Name = "G", Slug = "g", Description = "", CreatedByUserId = user.Id };
        db.Forums.Add(forum);
        var asset = new MediaAsset
        {
            UploaderUserId = user.Id,
            StorageKey = "emotes/pog",
            ContentType = "image/png",
            ByteSize = 800
        };
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();

        var service = new EmoteService(db, new AllowAllPermissions(), new FakeMedia(), new NeverBanned(), new FakeNode());
        var emote = await service.CreateAsync(user.Id, new CreateEmoteRequest("pog", asset.Id, forum.Id, "Pog"));
        var updated = await service.UpdateAsync(
            user.Id,
            emote.Id,
            new UpdateEmoteRequest { Name = "Pogchamp", Code = "pogchamp", SkinsUnicode = "🔥" });
        Assert.Equal("pogchamp", updated.Code);
        Assert.Equal("Pogchamp", updated.Name);
        Assert.Equal("🔥", updated.SkinsUnicode);
    }

    [Fact]
    public async Task ListAvailable_SeedsHighlightedUnicodeDefaults()
    {
        await using var db = CreateDb();
        var user = new User { Username = "erin", DisplayName = "Erin", Email = "erin@example.com", PasswordHash = "x" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var forum = new Forum { Name = "G", Slug = "g", Description = "", CreatedByUserId = user.Id };
        db.Forums.Add(forum);
        await db.SaveChangesAsync();

        var service = new EmoteService(db, new AllowAllPermissions(), new FakeMedia(), new NeverBanned(), new FakeNode());
        var first = await service.ListAvailableAsync(forum.Id, user.Id);
        Assert.Equal(6, first.Count(e => e.Highlighted));
        Assert.All(first.Where(e => e.Highlighted), e => Assert.False(string.IsNullOrEmpty(e.SkinsUnicode)));

        var thumb = first.Single(e => e.Code == "thumbsup");
        await service.DeleteAsync(user.Id, thumb.Id);
        var again = await service.ListAvailableAsync(forum.Id, user.Id);
        Assert.DoesNotContain(again, e => e.Code == "thumbsup");
    }

    [Fact]
    public async Task Reorder_UpdatesSortOrder()
    {
        await using var db = CreateDb();
        var user = new User { Username = "finn", DisplayName = "Finn", Email = "finn@example.com", PasswordHash = "x" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var forum = new Forum { Name = "G", Slug = "g", Description = "", CreatedByUserId = user.Id };
        db.Forums.Add(forum);
        await db.SaveChangesAsync();
        var service = new EmoteService(db, new AllowAllPermissions(), new FakeMedia(), new NeverBanned(), new FakeNode());
        var seeded = await service.ListAvailableAsync(forum.Id, user.Id);
        var reversed = seeded.Select(e => e.Id).Reverse().ToArray();
        await service.ReorderAsync(user.Id, new ReorderEmotesRequest(reversed, forum.Id));
        var next = await service.ListAvailableAsync(forum.Id, user.Id);
        Assert.Equal(reversed[0], next[0].Id);
    }

    private static SneklDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SneklDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SneklDbContext(options);
    }

    private sealed class FakeMedia : IMediaService
    {
        public Task<MediaAttachmentDto> UploadAsync(Guid userId, Stream content, string contentType, string? fileName, long contentLength, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<(Stream Stream, string ContentType, long ContentLength, bool CachePublic)?> OpenOriginalAsync(Guid mediaId, Guid? viewerUserId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<(Stream Stream, string ContentType, long ContentLength)?> OpenPreviewAsync(Guid mediaId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<MediaAttachmentDto>>> GetAttachmentsByPostIdsAsync(IEnumerable<Guid> postIds, Guid? viewerUserId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task AttachToPostAsync(
            Guid postId,
            Guid uploaderUserId,
            IReadOnlyList<Guid> mediaAssetIds,
            IReadOnlyList<NsfwMediaHintDto>? nsfwHints = null,
            CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<string>> GetAttachmentUrlsForPostAsync(Guid postId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<HubMediaPreview?> GetHubPreviewForPostAsync(Guid postId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task SetNsfwAsync(
            Guid actorUserId,
            Guid postId,
            Guid mediaAssetId,
            bool isNsfw,
            string? coverText,
            CancellationToken ct = default)
            => throw new NotImplementedException();

        public string BuildPublicUrl(Guid mediaId) => $"http://test/api/media/{mediaId}";
        public string BuildPreviewUrl(Guid mediaId) => $"http://test/api/media/{mediaId}/preview";
    }

    private sealed class FakeNode : INodeService
    {
        public Task<NodeConfig> GetOrCreateAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<NodeInfoDto> GetInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new NodeInfoDto(Guid.Empty, "did:web:test", "Test Node", "http://test", "", DateTime.UtcNow));
        public Task<NodeConfig> UpdateAsync(string name, string baseUrl, string description, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<int> GetVoteAlertBaseAsync(CancellationToken ct = default) => Task.FromResult(10);
        public Task<int> SetVoteAlertBaseAsync(int value, CancellationToken ct = default) => Task.FromResult(value);
        public byte[] GetSigningPrivateKey(NodeConfig node) => throw new NotImplementedException();
        public string GetVerificationMethodId(NodeConfig node) => throw new NotImplementedException();
    }

    private sealed class NeverBanned : IForumModerationService
    {
        public Task<bool> IsBannedAsync(Guid forumId, Guid userId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<ForumBanDto>> ListBansAsync(Guid forumId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ForumBanDto>>([]);
        public Task BanUserAsync(Guid forumId, Guid moderatorUserId, string username, string? reason, DateTime? expiresAt, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task UnbanUserAsync(Guid forumId, Guid userId, Guid? actorUserId = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<PostReportDto> ReportPostAsync(Guid reporterUserId, Guid postId, string reason, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<PostReportDto>> ListOpenReportsAsync(Guid forumId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PostReportDto>>([]);
        public Task DismissReportAsync(Guid moderatorUserId, Guid reportId, CancellationToken ct = default) => Task.CompletedTask;
        public Task ResolveReportAsync(Guid moderatorUserId, Guid reportId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class AllowAllPermissions : IPermissionService
    {
        public Task<bool> IsSuperUserAsync(Guid? userId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> IsServerOwnerAsync(Guid? userId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CanOverrideForumOwnerAsync(Guid? userId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<Dictionary<string, GrantMode>> GetServerPermissionModesAsync(Guid? userId, CancellationToken ct = default) =>
            Task.FromResult(new Dictionary<string, GrantMode>());
        public Task<Dictionary<string, GrantMode>> GetForumPermissionModesAsync(Guid? userId, Guid forumId, CancellationToken ct = default) =>
            Task.FromResult(new Dictionary<string, GrantMode>());
        public Task<ServerPermissions> GetServerPermissionsAsync(Guid? userId, CancellationToken ct = default) =>
            Task.FromResult(ServerPermissions.None);
        public Task<ForumPermissions> GetForumPermissionsAsync(Guid? userId, Guid forumId, CancellationToken ct = default) =>
            Task.FromResult(ForumPermissions.None);
        public Task RequireServerPermissionAsync(Guid userId, ServerPermissions flag, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task RequireForumPermissionAsync(Guid userId, Guid forumId, ForumPermissions flag, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task EnsureBotMayAttachMediaAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CanSeeForumAsync(Guid? userId, Guid forumId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CanViewForumAsync(Guid? userId, Guid forumId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> HasForumRoleAssignmentAsync(Guid? userId, Guid forumId, CancellationToken ct = default) =>
            Task.FromResult(true);
        public Task<bool> ViewerCanManageFromRolesAsync(Guid? userId, Guid forumId, CancellationToken ct = default) =>
            Task.FromResult(true);
        public Task<Dictionary<string, GrantMode>> GetWatercoolerPermissionModesAsync(Guid? userId, Guid watercoolerId, CancellationToken ct = default) =>
            Task.FromResult(new Dictionary<string, GrantMode>());
        public Task<WatercoolerPermissions> GetWatercoolerPermissionsAsync(Guid? userId, Guid watercoolerId, CancellationToken ct = default) =>
            Task.FromResult(WatercoolerPermissions.None);
        public Task RequireWatercoolerPermissionAsync(Guid userId, Guid watercoolerId, WatercoolerPermissions flag, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task<bool> CanSeeWatercoolerAsync(Guid? userId, Guid watercoolerId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CanViewWatercoolerAsync(Guid? userId, Guid watercoolerId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CanSpeakWatercoolerAsync(Guid? userId, Guid watercoolerId, CancellationToken ct = default) => Task.FromResult(true);
    }
}
