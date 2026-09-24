using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class ForumMemberServiceTests
{
    [Fact]
    public async Task UpdateFlairTemplate_ChangesEmojiAndLabel()
    {
        await using var db = CreateDb();
        var forum = AddForum(db);
        var template = new ForumFlairTemplate
        {
            ForumId = forum.Id,
            Emoji = "🐱",
            Label = "Cat",
            Position = 0
        };
        db.ForumFlairTemplates.Add(template);
        await db.SaveChangesAsync();

        var service = new ForumMemberService(db);
        var updated = await service.UpdateFlairTemplateAsync(forum.Id, template.Id, new UpdateForumFlairTemplateRequest(
            "🐸",
            null,
            "Frog",
            null,
            null));

        Assert.Equal("🐸", updated.Emoji);
        Assert.Equal("Frog", updated.Label);
        Assert.Equal(0, updated.Position);
    }

    [Fact]
    public async Task DeleteFlairTemplate_ClearsMemberAssignments()
    {
        await using var db = CreateDb();
        var forum = AddForum(db);
        var user = new User
        {
            Username = "member",
            DisplayName = "Member",
            Email = "member@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        var template = new ForumFlairTemplate { ForumId = forum.Id, Emoji = "★", Label = "Star", Position = 0 };
        db.ForumFlairTemplates.Add(template);
        await db.SaveChangesAsync();
        db.ForumMemberFlairs.Add(new ForumMemberFlair { ForumId = forum.Id, UserId = user.Id, TemplateId = template.Id });
        await db.SaveChangesAsync();

        var service = new ForumMemberService(db);
        await service.DeleteFlairTemplateAsync(forum.Id, template.Id);

        Assert.Empty(await db.ForumFlairTemplates.ToListAsync());
        Assert.Empty(await db.ForumMemberFlairs.ToListAsync());
    }

    [Fact]
    public async Task UpdateForumSettings_SetsAndClearsAvatarAndBanner()
    {
        await using var db = CreateDb();
        var forum = AddForum(db);
        var user = new User
        {
            Username = "owner",
            DisplayName = "Owner",
            Email = "owner@example.com",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var avatar = new MediaAsset
        {
            UploaderUserId = user.Id,
            StorageKey = "avatar",
            ContentType = "image/png",
            ByteSize = 12
        };
        var banner = new MediaAsset
        {
            UploaderUserId = user.Id,
            StorageKey = "banner",
            ContentType = "image/jpeg",
            ByteSize = 40
        };
        db.MediaAssets.AddRange(avatar, banner);
        await db.SaveChangesAsync();

        var service = new ForumMemberService(db);
        await service.UpdateForumSettingsAsync(
            forum.Id,
            new UpdateForumSettingsRequest(null, null, null, AvatarMediaId: avatar.Id, BannerMediaId: banner.Id),
            user.Id);

        var updated = await db.Forums.SingleAsync();
        Assert.Equal(avatar.Id, updated.AvatarMediaAssetId);
        Assert.Equal(banner.Id, updated.BannerMediaAssetId);

        await service.UpdateForumSettingsAsync(
            forum.Id,
            new UpdateForumSettingsRequest(null, null, null, ClearAvatar: true, ClearBanner: true),
            user.Id);

        updated = await db.Forums.SingleAsync();
        Assert.Null(updated.AvatarMediaAssetId);
        Assert.Null(updated.BannerMediaAssetId);
    }

    [Fact]
    public async Task UpdateForumSettings_RejectsUnknownMedia()
    {
        await using var db = CreateDb();
        var forum = AddForum(db);
        var service = new ForumMemberService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateForumSettingsAsync(
                forum.Id,
                new UpdateForumSettingsRequest(null, null, null, AvatarMediaId: Guid.NewGuid()),
                Guid.NewGuid()));

        Assert.Equal("Media asset not found.", ex.Message);
    }

    private static Forum AddForum(SneklDbContext db)
    {
        var forum = new Forum
        {
            Name = "General",
            Slug = "general",
            Description = "d",
            CreatedByUserId = Guid.NewGuid()
        };
        db.Forums.Add(forum);
        return forum;
    }

    private static SneklDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SneklDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SneklDbContext(options);
    }
}
