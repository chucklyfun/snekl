using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Emotes;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IEmoteService
{
    Task<IReadOnlyList<EmoteDto>> ListAvailableAsync(
        Guid? forumId,
        Guid? viewerUserId,
        bool allViewable = false,
        CancellationToken ct = default);
    Task<EmoteDto> CreateAsync(Guid userId, CreateEmoteRequest request, CancellationToken ct = default);
    Task<EmoteDto> UpdateAsync(Guid userId, Guid emoteId, UpdateEmoteRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid emoteId, CancellationToken ct = default);
    Task ReorderAsync(Guid userId, ReorderEmotesRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PostReactionDto>> ToggleReactionAsync(
        Guid postId,
        Guid userId,
        ToggleReactionRequest request,
        CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<PostReactionDto>>> GetReactionsByPostIdsAsync(
        IEnumerable<Guid> postIds,
        Guid? viewerUserId,
        CancellationToken ct = default);
}

public class EmoteService : IEmoteService
{
    private readonly SneklDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly IMediaService _media;
    private readonly IForumModerationService _moderation;
    private readonly INodeService _node;

    public EmoteService(
        SneklDbContext db,
        IPermissionService permissions,
        IMediaService media,
        IForumModerationService moderation,
        INodeService node)
    {
        _db = db;
        _permissions = permissions;
        _media = media;
        _moderation = moderation;
        _node = node;
    }

    public async Task<IReadOnlyList<EmoteDto>> ListAvailableAsync(
        Guid? forumId,
        Guid? viewerUserId,
        bool allViewable = false,
        CancellationToken ct = default)
    {
        if (forumId is Guid fid && !await _permissions.CanViewForumAsync(viewerUserId, fid, ct))
            throw new PermissionDeniedException(ForumPermissions.ViewForum.ToString(), "forum");

        var insertOtherForums = allViewable && viewerUserId.HasValue;
        if (!insertOtherForums && viewerUserId.HasValue)
        {
            if (await _permissions.IsSuperUserAsync(viewerUserId, ct))
                insertOtherForums = true;
            else if (forumId is Guid destForum)
            {
                var dest = await _permissions.GetForumPermissionsAsync(viewerUserId, destForum, ct);
                insertOtherForums = dest.HasFlag(ForumPermissions.UseOtherForumEmotes);
            }
            else
            {
                var dest = await _permissions.GetServerPermissionsAsync(viewerUserId, ct);
                insertOtherForums = dest.HasFlag(ServerPermissions.UseOtherForumEmotes);
            }
        }

        await EnsureDefaultReactionsAsync(forumId, ct);

        var rows = await _db.Emotes.AsNoTracking().Include(e => e.Forum).ToListAsync(ct);
        var node = await _node.GetInfoAsync(ct);
        var serverPrefix = EmoteSyntax.NormalizeQualifier(node.Name) ?? "server";
        var viewCache = new Dictionary<Guid, bool>();
        var selected = new List<EmoteDto>();

        foreach (var emote in rows.OrderBy(e => e.SortOrder).ThenBy(e => e.ForumId == null ? 1 : 0).ThenBy(e => e.Code))
        {
            if (emote.ForumId is null)
            {
                selected.Add(ToDto(emote, node.Did, serverPrefix, canInsert: true));
                continue;
            }

            var isCurrentForum = forumId is Guid current && emote.ForumId == current;
            if (isCurrentForum)
            {
                selected.Add(ToDto(emote, node.Did, serverPrefix, canInsert: true));
                continue;
            }

            if (!viewCache.TryGetValue(emote.ForumId.Value, out var canView))
            {
                canView = await _permissions.CanViewForumAsync(viewerUserId, emote.ForumId.Value, ct);
                viewCache[emote.ForumId.Value] = canView;
            }

            if (!canView) continue;
            selected.Add(ToDto(emote, node.Did, serverPrefix, canInsert: insertOtherForums));
        }

        return selected;
    }

    public async Task<EmoteDto> CreateAsync(Guid userId, CreateEmoteRequest request, CancellationToken ct = default)
    {
        if (!EmoteSyntax.TryNormalizeCode(request.Code, out var code))
            throw new InvalidOperationException("Emote code must be 2–32 characters: lowercase letters, digits, or underscore.");

        var name = string.IsNullOrWhiteSpace(request.Name) ? code : request.Name.Trim();
        if (name.Length > 64)
            throw new InvalidOperationException("Emote name is too long.");

        if (request.ForumId is Guid forumId)
            await RequireManageForumEmotesAsync(userId, forumId, ct);
        else
            await RequireManageServerEmotesAsync(userId, ct);

        var scopedCount = await _db.Emotes.CountAsync(e => e.ForumId == request.ForumId, ct);
        if (scopedCount >= EmoteSyntax.MaxEmotesPerScope)
            throw new InvalidOperationException($"This catalog already has {EmoteSyntax.MaxEmotesPerScope} emotes.");

        if (request.MediaAssetId is null && string.IsNullOrEmpty(EmoteSyntax.NormalizeSkinEmoji(request.SkinsUnicode)))
            throw new InvalidOperationException("Upload an image or choose a unicode emoji.");

        var taken = await _db.Emotes.AnyAsync(
            e => e.Code == code && e.ForumId == request.ForumId,
            ct);
        if (taken)
            throw new InvalidOperationException($"Emote :{code}: already exists in this catalog.");

        MediaAsset? asset = null;
        if (request.MediaAssetId is Guid mediaId)
            asset = await RequireEmoteAssetAsync(userId, mediaId, exceptEmoteId: null, ct);

        var nextOrder = await _db.Emotes.Where(e => e.ForumId == request.ForumId).Select(e => (int?)e.SortOrder).MaxAsync(ct) ?? -1;

        var emote = new Emote
        {
            Code = code,
            Name = name,
            MediaAssetId = asset?.Id,
            ForumId = request.ForumId,
            CreatedByUserId = userId,
            IsAnimated = asset is not null && string.Equals(asset.ContentType, "image/gif", StringComparison.OrdinalIgnoreCase),
            SkinsUnicode = EmoteSyntax.NormalizeSkinEmoji(request.SkinsUnicode),
            SortOrder = nextOrder + 1,
            Highlighted = request.Highlighted
        };
        await ClaimSkinAsync(emote.ForumId, emote.SkinsUnicode, exceptEmoteId: null, ct);
        _db.Emotes.Add(emote);
        await _db.SaveChangesAsync(ct);
        return await ToDtoAsync(emote, canInsert: true, ct);
    }

    public async Task<EmoteDto> UpdateAsync(Guid userId, Guid emoteId, UpdateEmoteRequest request, CancellationToken ct = default)
    {
        var emote = await _db.Emotes.FirstOrDefaultAsync(e => e.Id == emoteId, ct)
            ?? throw new InvalidOperationException("Emote not found.");

        if (emote.ForumId is Guid forumId)
            await RequireManageForumEmotesAsync(userId, forumId, ct);
        else
            await RequireManageServerEmotesAsync(userId, ct);

        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            if (!EmoteSyntax.TryNormalizeCode(request.Code, out var code))
                throw new InvalidOperationException("Emote code must be 2–32 characters: lowercase letters, digits, or underscore.");
            if (code != emote.Code)
            {
                var taken = await _db.Emotes.AnyAsync(
                    e => e.Code == code && e.ForumId == emote.ForumId && e.Id != emote.Id,
                    ct);
                if (taken)
                    throw new InvalidOperationException($"Emote :{code}: already exists in this catalog.");
                emote.Code = code;
            }
        }

        if (request.Name is not null)
        {
            var name = string.IsNullOrWhiteSpace(request.Name) ? emote.Code : request.Name.Trim();
            if (name.Length > 64)
                throw new InvalidOperationException("Emote name is too long.");
            emote.Name = name;
        }

        if (request.MediaAssetId is Guid mediaId && mediaId != emote.MediaAssetId)
        {
            var asset = await RequireEmoteAssetAsync(userId, mediaId, emote.Id, ct);
            emote.MediaAssetId = asset.Id;
            emote.IsAnimated = string.Equals(asset.ContentType, "image/gif", StringComparison.OrdinalIgnoreCase);
        }

        if (request.Highlighted is bool highlighted)
            emote.Highlighted = highlighted;

        if (request.ClearSkin)
            emote.SkinsUnicode = null;
        else if (request.SkinsUnicode is not null)
            emote.SkinsUnicode = EmoteSyntax.NormalizeSkinEmoji(request.SkinsUnicode);

        await ClaimSkinAsync(emote.ForumId, emote.SkinsUnicode, emote.Id, ct);
        await _db.SaveChangesAsync(ct);
        return await ToDtoAsync(emote, canInsert: true, ct);
    }

    public async Task DeleteAsync(Guid userId, Guid emoteId, CancellationToken ct = default)
    {
        var emote = await _db.Emotes.FirstOrDefaultAsync(e => e.Id == emoteId, ct)
            ?? throw new InvalidOperationException("Emote not found.");

        if (emote.ForumId is Guid forumId)
            await RequireManageForumEmotesAsync(userId, forumId, ct);
        else
            await RequireManageServerEmotesAsync(userId, ct);

        _db.Emotes.Remove(emote);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ReorderAsync(Guid userId, ReorderEmotesRequest request, CancellationToken ct = default)
    {
        if (request.ForumId is Guid forumId)
            await RequireManageForumEmotesAsync(userId, forumId, ct);
        else
            await RequireManageServerEmotesAsync(userId, ct);

        var rows = await _db.Emotes.Where(e => e.ForumId == request.ForumId).ToListAsync(ct);
        if (request.Ids.Length != rows.Count)
            throw new InvalidOperationException("Reorder list must include every emote in this catalog.");

        var byId = rows.ToDictionary(e => e.Id);
        for (var i = 0; i < request.Ids.Length; i++)
        {
            if (!byId.TryGetValue(request.Ids[i], out var emote))
                throw new InvalidOperationException("Reorder list does not match this catalog.");
            emote.SortOrder = i;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PostReactionDto>> ToggleReactionAsync(
        Guid postId,
        Guid userId,
        ToggleReactionRequest request,
        CancellationToken ct = default)
    {
        var hasEmote = request.EmoteId.HasValue;
        var emoji = request.Emoji?.Trim();
        var hasEmoji = !string.IsNullOrEmpty(emoji);
        if (hasEmote == hasEmoji)
            throw new InvalidOperationException("React with either a custom emote or a unicode emoji.");

        if (hasEmoji && !EmoteSyntax.IsValidUnicodeEmoji(emoji))
            throw new InvalidOperationException("That is not a valid emoji reaction.");

        var post = await _db.Posts.FirstOrDefaultAsync(p => p.Id == postId, ct)
            ?? throw new InvalidOperationException("Post not found.");

        if (post.IsDeleted)
            throw new InvalidOperationException("Cannot react to a deleted post.");

        if (post.IsFederated)
            throw new InvalidOperationException("React on the origin node that hosts this post.");

        if (post.WatercoolerId.HasValue)
        {
            await _permissions.RequireWatercoolerPermissionAsync(userId, post.WatercoolerId.Value, WatercoolerPermissions.ViewWatercooler, ct);
            var cooler = await _db.Watercoolers.AsNoTracking().FirstOrDefaultAsync(w => w.Id == post.WatercoolerId.Value, ct);
            if (cooler?.ParentForumId is Guid parentForum && await _moderation.IsBannedAsync(parentForum, userId, ct))
                throw new InvalidOperationException("You are banned from this forum.");
        }
        else
        {
            if (!post.ForumId.HasValue)
                throw new InvalidOperationException("Post not found.");
            await _permissions.RequireForumPermissionAsync(userId, post.ForumId.Value, ForumPermissions.ViewForum, ct);
            if (await _moderation.IsBannedAsync(post.ForumId.Value, userId, ct))
                throw new InvalidOperationException("You are banned from this forum.");
        }

        Emote? emote = null;
        if (request.EmoteId is Guid emoteId)
        {
            emote = await _db.Emotes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == emoteId, ct)
                ?? throw new InvalidOperationException("Emote not found.");
            if (emote.ForumId is Guid emoteForum)
            {
                var destForum = post.ForumId;
                if (destForum is Guid postForum && emoteForum != postForum)
                {
                    if (!await _permissions.IsSuperUserAsync(userId, ct))
                    {
                        var dest = await _permissions.GetForumPermissionsAsync(userId, postForum, ct);
                        if (!dest.HasFlag(ForumPermissions.UseOtherForumEmotes))
                            throw new InvalidOperationException("You cannot use emotes from other forums here.");
                    }
                    if (!await _permissions.CanViewForumAsync(userId, emoteForum, ct))
                        throw new InvalidOperationException("That emote is not available.");
                }
            }
        }

        var existing = hasEmote
            ? await _db.PostReactions.FirstOrDefaultAsync(
                r => r.PostId == postId && r.UserId == userId && r.EmoteId == request.EmoteId, ct)
            : await _db.PostReactions.FirstOrDefaultAsync(
                r => r.PostId == postId && r.UserId == userId && r.Emoji == emoji, ct);

        if (existing is not null)
            _db.PostReactions.Remove(existing);
        else
        {
            _db.PostReactions.Add(new PostReaction
            {
                PostId = postId,
                UserId = userId,
                EmoteId = emote?.Id,
                Emoji = hasEmoji ? emoji : null
            });
        }

        await _db.SaveChangesAsync(ct);
        var map = await GetReactionsByPostIdsAsync([postId], userId, ct);
        return map.TryGetValue(postId, out var list) ? list : [];
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<PostReactionDto>>> GetReactionsByPostIdsAsync(
        IEnumerable<Guid> postIds,
        Guid? viewerUserId,
        CancellationToken ct = default)
    {
        var ids = postIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<PostReactionDto>>();

        var rows = await _db.PostReactions
            .AsNoTracking()
            .Include(r => r.Emote)
            .Where(r => ids.Contains(r.PostId))
            .ToListAsync(ct);

        var posts = await _db.Posts.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.ForumId })
            .ToListAsync(ct);
        var forumIds = posts.Where(p => p.ForumId.HasValue).Select(p => p.ForumId!.Value).Distinct().ToList();

        var skins = await _db.Emotes.AsNoTracking()
            .Where(e => e.SkinsUnicode != null && (e.ForumId == null || forumIds.Contains(e.ForumId.Value)))
            .ToListAsync(ct);

        var result = new Dictionary<Guid, IReadOnlyList<PostReactionDto>>();
        foreach (var group in rows.GroupBy(r => r.PostId))
        {
            var forumId = posts.FirstOrDefault(p => p.Id == group.Key)?.ForumId;
            var summaries = group
                .GroupBy(ReactionKey)
                .Select(g =>
                {
                    var sample = g.First();
                    var viewerReacted = viewerUserId is Guid vid && g.Any(r => r.UserId == vid);
                    var imageUrl = sample.Emote is not null
                        ? ImageUrl(sample.Emote)
                        : SkinImageUrl(skins, forumId, sample.Emoji);
                    return new PostReactionDto(
                        g.Key,
                        sample.EmoteId,
                        sample.Emote?.Code,
                        sample.Emoji,
                        imageUrl,
                        g.Count(),
                        viewerReacted);
                })
                .OrderByDescending(r => r.Count)
                .ThenBy(r => r.Code ?? r.Emoji, StringComparer.Ordinal)
                .ToList();
            result[group.Key] = summaries;
        }

        return result;
    }

    private async Task ClaimSkinAsync(Guid? forumId, string? skinsUnicode, Guid? exceptEmoteId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(skinsUnicode)) return;
        var key = EmoteSyntax.EmojiMatchKey(skinsUnicode);
        var others = await _db.Emotes
            .Where(e => e.ForumId == forumId && e.SkinsUnicode != null && (exceptEmoteId == null || e.Id != exceptEmoteId))
            .ToListAsync(ct);
        foreach (var other in others)
        {
            if (EmoteSyntax.EmojiMatchKey(other.SkinsUnicode!) == key)
                other.SkinsUnicode = null;
        }
    }

    private string? SkinImageUrl(IReadOnlyList<Emote> skins, Guid? forumId, string? emoji)
    {
        if (string.IsNullOrEmpty(emoji)) return null;
        var key = EmoteSyntax.EmojiMatchKey(emoji);
        var match = skins.FirstOrDefault(e =>
                e.ForumId == forumId && e.SkinsUnicode is not null && EmoteSyntax.EmojiMatchKey(e.SkinsUnicode) == key)
            ?? skins.FirstOrDefault(e =>
                e.ForumId == null && e.SkinsUnicode is not null && EmoteSyntax.EmojiMatchKey(e.SkinsUnicode) == key);
        return match is null || match.MediaAssetId is not Guid mediaId ? null : _media.BuildPublicUrl(mediaId);
    }

    private async Task EnsureDefaultReactionsAsync(Guid? forumId, CancellationToken ct)
    {
        if (forumId is Guid fid)
        {
            var forum = await _db.Forums.FirstOrDefaultAsync(f => f.Id == fid, ct);
            if (forum is null || forum.EmoteDefaultsSeeded) return;
            await InsertDefaultReactionsAsync(fid, forum.CreatedByUserId, ct);
            forum.EmoteDefaultsSeeded = true;
            await _db.SaveChangesAsync(ct);
            return;
        }

        var node = await _db.NodeConfig.FirstOrDefaultAsync(ct);
        if (node is null || node.EmoteDefaultsSeeded) return;
        var creator = await _db.Users.OrderBy(u => u.CreatedAt).FirstOrDefaultAsync(ct);
        if (creator is null) return;
        await InsertDefaultReactionsAsync(null, creator.Id, ct);
        node.EmoteDefaultsSeeded = true;
        await _db.SaveChangesAsync(ct);
    }

    private async Task InsertDefaultReactionsAsync(Guid? forumId, Guid createdByUserId, CancellationToken ct)
    {
        var existing = await _db.Emotes.Where(e => e.ForumId == forumId).Select(e => e.Code).ToListAsync(ct);
        var taken = existing.ToHashSet(StringComparer.Ordinal);
        var nextOrder = existing.Count == 0
            ? 0
            : await _db.Emotes.Where(e => e.ForumId == forumId).MaxAsync(e => e.SortOrder, ct) + 1;
        foreach (var item in EmoteSyntax.DefaultHighlightedReactions)
        {
            if (taken.Contains(item.Code)) continue;
            _db.Emotes.Add(new Emote
            {
                Code = item.Code,
                Name = item.Name,
                ForumId = forumId,
                CreatedByUserId = createdByUserId,
                SkinsUnicode = item.Emoji,
                SortOrder = nextOrder++,
                Highlighted = true
            });
        }
    }

    private async Task<MediaAsset> RequireEmoteAssetAsync(
        Guid userId,
        Guid mediaAssetId,
        Guid? exceptEmoteId,
        CancellationToken ct)
    {
        var asset = await _db.MediaAssets.FirstOrDefaultAsync(a => a.Id == mediaAssetId, ct)
            ?? throw new InvalidOperationException("Media asset not found.");

        if (asset.UploaderUserId != userId && !await _permissions.IsSuperUserAsync(userId, ct))
            throw new InvalidOperationException("You can only register emotes from media you uploaded.");

        if (asset.ByteSize > EmoteSyntax.MaxEmoteBytes)
            throw new InvalidOperationException($"Emotes must be {EmoteSyntax.MaxEmoteBytes / 1024} KB or smaller.");

        var usedAsEmote = await _db.Emotes.AnyAsync(
            e => e.MediaAssetId == asset.Id && (exceptEmoteId == null || e.Id != exceptEmoteId),
            ct);
        if (usedAsEmote)
            throw new InvalidOperationException("That image is already registered as an emote.");

        if (await _db.PostMedia.AnyAsync(pm => pm.MediaAssetId == asset.Id, ct))
            throw new InvalidOperationException("That image is already attached to a post.");

        return asset;
    }

    private async Task RequireManageForumEmotesAsync(Guid userId, Guid forumId, CancellationToken ct)
    {
        if (await _permissions.IsSuperUserAsync(userId, ct)) return;
        await _permissions.RequireForumPermissionAsync(userId, forumId, ForumPermissions.ManageForum, ct);
    }

    private async Task RequireManageServerEmotesAsync(Guid userId, CancellationToken ct)
    {
        if (await _permissions.IsSuperUserAsync(userId, ct)) return;
        await _permissions.RequireServerPermissionAsync(userId, ServerPermissions.ManageServerSettings, ct);
    }

    private async Task<EmoteDto> ToDtoAsync(Emote emote, bool canInsert, CancellationToken ct)
    {
        var node = await _node.GetInfoAsync(ct);
        var prefix = EmoteSyntax.NormalizeQualifier(node.Name) ?? "server";
        string? slug = emote.Forum?.Slug;
        string? forumName = emote.Forum?.Name;
        if (emote.ForumId is Guid forumId && slug is null)
        {
            var row = await _db.Forums.AsNoTracking()
                .Where(f => f.Id == forumId)
                .Select(f => new { f.Slug, f.Name })
                .FirstOrDefaultAsync(ct);
            slug = row?.Slug;
            forumName = row?.Name;
        }

        return new EmoteDto(
            emote.Id,
            emote.Code,
            emote.Name,
            ImageUrl(emote),
            emote.ForumId,
            emote.IsAnimated,
            emote.CreatedAt,
            emote.SkinsUnicode,
            slug,
            forumName,
            node.Did,
            prefix,
            canInsert,
            emote.SortOrder,
            emote.Highlighted);
    }

    private EmoteDto ToDto(Emote emote, string serverDid, string serverPrefix, bool canInsert) => new(
        emote.Id,
        emote.Code,
        emote.Name,
        ImageUrl(emote),
        emote.ForumId,
        emote.IsAnimated,
        emote.CreatedAt,
        emote.SkinsUnicode,
        emote.Forum?.Slug,
        emote.Forum?.Name,
        serverDid,
        serverPrefix,
        canInsert,
        emote.SortOrder,
        emote.Highlighted);

    private string ImageUrl(Emote emote) =>
        emote.MediaAssetId is Guid mediaId ? _media.BuildPublicUrl(mediaId) : string.Empty;

    private static string ReactionKey(PostReaction reaction) =>
        reaction.EmoteId is Guid emoteId ? $"e:{emoteId:D}" : $"u:{reaction.Emoji}";
}
