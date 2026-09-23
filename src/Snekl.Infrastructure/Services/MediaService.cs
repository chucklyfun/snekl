using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Snekl.Domain;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Media;
using Snekl.Infrastructure.Storage;

namespace Snekl.Infrastructure.Services;

public record HubMediaPreview(string? ImagePreviewUrl, string? NsfwCover);

public interface IMediaService
{
    Task<MediaAttachmentDto> UploadAsync(Guid userId, Stream content, string contentType, string? fileName, long contentLength, CancellationToken ct = default);
    Task<(Stream Stream, string ContentType, long ContentLength, bool CachePublic)?> OpenOriginalAsync(Guid mediaId, Guid? viewerUserId, CancellationToken ct = default);
    Task<(Stream Stream, string ContentType, long ContentLength)?> OpenPreviewAsync(Guid mediaId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<MediaAttachmentDto>>> GetAttachmentsByPostIdsAsync(
        IEnumerable<Guid> postIds,
        Guid? viewerUserId,
        CancellationToken ct = default);
    Task AttachToPostAsync(
        Guid postId,
        Guid uploaderUserId,
        IReadOnlyList<Guid> mediaAssetIds,
        IReadOnlyList<NsfwMediaHintDto>? nsfwHints = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAttachmentUrlsForPostAsync(Guid postId, CancellationToken ct = default);
    Task<HubMediaPreview?> GetHubPreviewForPostAsync(Guid postId, CancellationToken ct = default);
    Task SetNsfwAsync(
        Guid actorUserId,
        Guid postId,
        Guid mediaAssetId,
        bool isNsfw,
        string? coverText,
        CancellationToken ct = default);
    string BuildPublicUrl(Guid mediaId);
    string BuildPreviewUrl(Guid mediaId);
}

public class MediaService : IMediaService
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif"
    };

    private readonly SneklDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly IObjectStorage _storage;
    private readonly ObjectStorageOptions _options;
    private readonly string _publicBaseUrl;

    public MediaService(
        SneklDbContext db,
        IPermissionService permissions,
        IObjectStorage storage,
        IOptions<ObjectStorageOptions> options,
        IConfiguration configuration)
    {
        _db = db;
        _permissions = permissions;
        _storage = storage;
        _options = options.Value;
        _publicBaseUrl = (configuration["Node:BaseUrl"] ?? "http://localhost:5001").TrimEnd('/');
    }

    public async Task<MediaAttachmentDto> UploadAsync(
        Guid userId,
        Stream content,
        string contentType,
        string? fileName,
        long contentLength,
        CancellationToken ct = default)
    {
        if (contentLength <= 0)
            throw new InvalidOperationException("Empty uploads are not allowed.");

        await _permissions.EnsureBotMayAttachMediaAsync(userId, ct);

        if (contentLength > _options.MaxUploadBytes)
            throw new InvalidOperationException($"File exceeds maximum size of {_options.MaxUploadBytes} bytes.");

        var normalizedType = NormalizeContentType(contentType, fileName);
        if (!AllowedContentTypes.Contains(normalizedType))
            throw new InvalidOperationException("Only JPEG, PNG, WebP, and GIF images are allowed.");

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length > _options.MaxUploadBytes)
            throw new InvalidOperationException($"File exceeds maximum size of {_options.MaxUploadBytes} bytes.");

        buffer.Position = 0;
        if (!LooksLikeImage(buffer, normalizedType))
            throw new InvalidOperationException("File content does not match an allowed image type.");

        buffer.Position = 0;
        var assetId = Guid.NewGuid();
        var extension = ExtensionFor(normalizedType);
        var storageKey = $"{userId:N}/{assetId:N}{extension}";

        await _storage.PutAsync(storageKey, buffer, normalizedType, buffer.Length, ct);

        var asset = new MediaAsset
        {
            Id = assetId,
            UploaderUserId = userId,
            StorageKey = storageKey,
            ContentType = normalizedType,
            ByteSize = buffer.Length
        };

        _db.MediaAssets.Add(asset);
        await _db.SaveChangesAsync(ct);

        return ToDto(asset, null, canViewOriginal: true);
    }

    public async Task<(Stream Stream, string ContentType, long ContentLength, bool CachePublic)?> OpenOriginalAsync(
        Guid mediaId,
        Guid? viewerUserId,
        CancellationToken ct = default)
    {
        var asset = await _db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == mediaId, ct);
        if (asset is null) return null;
        if (!string.IsNullOrWhiteSpace(asset.ExternalUrl) || string.IsNullOrWhiteSpace(asset.StorageKey))
            return null;

        var link = await _db.PostMedia.AsNoTracking()
            .Include(pm => pm.Post)
            .FirstOrDefaultAsync(pm => pm.MediaAssetId == mediaId, ct);

        var nsfw = link?.IsNsfw == true;
        if (nsfw && !await CanAccessOriginalAsync(viewerUserId, link!, asset, ct))
            return null;

        var result = await _storage.GetAsync(asset.StorageKey, ct);
        if (result is null) return null;

        return (result.Value.Stream, asset.ContentType, result.Value.ContentLength, CachePublic: !nsfw);
    }

    public async Task<(Stream Stream, string ContentType, long ContentLength)?> OpenPreviewAsync(
        Guid mediaId,
        CancellationToken ct = default)
    {
        var link = await _db.PostMedia.AsNoTracking()
            .FirstOrDefaultAsync(pm => pm.MediaAssetId == mediaId && pm.IsNsfw, ct);
        if (link is null || string.IsNullOrWhiteSpace(link.CensorStorageKey))
            return null;

        var result = await _storage.GetAsync(link.CensorStorageKey, ct);
        if (result is null) return null;

        var contentType = string.IsNullOrWhiteSpace(link.CensorContentType)
            ? "image/jpeg"
            : link.CensorContentType;
        return (result.Value.Stream, contentType, result.Value.ContentLength);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<MediaAttachmentDto>>> GetAttachmentsByPostIdsAsync(
        IEnumerable<Guid> postIds,
        Guid? viewerUserId,
        CancellationToken ct = default)
    {
        var ids = postIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, IReadOnlyList<MediaAttachmentDto>>();

        var links = await _db.PostMedia
            .AsNoTracking()
            .Include(pm => pm.MediaAsset)
            .Include(pm => pm.Post)
            .Where(pm => ids.Contains(pm.PostId))
            .OrderBy(pm => pm.SortOrder)
            .ToListAsync(ct);

        var nsfwForums = links
            .Where(l => l.IsNsfw && l.Post?.ForumId is not null)
            .Select(l => l.Post!.ForumId!.Value)
            .Distinct()
            .ToList();
        var viewNsfwForums = new HashSet<Guid>();
        foreach (var forumId in nsfwForums)
        {
            var perms = await _permissions.GetForumPermissionsAsync(viewerUserId, forumId, ct);
            if (perms.HasFlag(ForumPermissions.ViewNsfwMedia))
                viewNsfwForums.Add(forumId);
        }

        return links
            .Where(l => l.MediaAsset is not null)
            .GroupBy(l => l.PostId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<MediaAttachmentDto>)g.Select(l =>
                {
                    var canView = CanAccessOriginal(viewerUserId, l, l.MediaAsset!, viewNsfwForums);
                    return ToDto(l.MediaAsset!, l, canView);
                }).ToList());
    }

    public async Task AttachToPostAsync(
        Guid postId,
        Guid uploaderUserId,
        IReadOnlyList<Guid> mediaAssetIds,
        IReadOnlyList<NsfwMediaHintDto>? nsfwHints = null,
        CancellationToken ct = default)
    {
        if (mediaAssetIds.Count == 0) return;

        var post = await _db.Posts.FirstOrDefaultAsync(p => p.Id == postId, ct)
            ?? throw new InvalidOperationException("Post not found.");

        var distinctIds = mediaAssetIds.Distinct().ToList();
        var assets = await _db.MediaAssets
            .Where(a => distinctIds.Contains(a.Id))
            .ToListAsync(ct);

        if (assets.Count != distinctIds.Count)
            throw new InvalidOperationException("One or more media assets were not found.");

        if (assets.Any(a => a.UploaderUserId != uploaderUserId))
            throw new InvalidOperationException("You can only attach media you uploaded.");

        var alreadyLinked = await _db.PostMedia.AnyAsync(pm => distinctIds.Contains(pm.MediaAssetId), ct);
        if (alreadyLinked)
            throw new InvalidOperationException("A media asset is already attached to a post.");

        var hints = (nsfwHints ?? [])
            .GroupBy(h => h.MediaAssetId)
            .ToDictionary(g => g.Key, g => g.Last());
        var allowNsfw = post.ForumId.HasValue && !post.WatercoolerId.HasValue;
        var assetById = assets.ToDictionary(a => a.Id);

        for (var i = 0; i < mediaAssetIds.Count; i++)
        {
            var mediaId = mediaAssetIds[i];
            var hasHint = hints.TryGetValue(mediaId, out var hint);
            var isNsfw = allowNsfw && hasHint;
            var cover = isNsfw ? NsfwMedia.RequireCover(hint!.CoverText) : null;
            var link = new PostMedia
            {
                PostId = postId,
                MediaAssetId = mediaId,
                SortOrder = i,
                IsNsfw = isNsfw,
                CoverText = cover
            };
            _db.PostMedia.Add(link);
            if (isNsfw)
                await EnsureCensorAsync(link, assetById[mediaId], ct);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetAttachmentUrlsForPostAsync(Guid postId, CancellationToken ct = default)
    {
        var links = await _db.PostMedia.AsNoTracking()
            .Include(pm => pm.MediaAsset)
            .Where(pm => pm.PostId == postId)
            .OrderBy(pm => pm.SortOrder)
            .ToListAsync(ct);

        var urls = new List<string>();
        foreach (var link in links)
        {
            if (link.MediaAsset is null) continue;
            if (link.IsNsfw)
            {
                if (!string.IsNullOrWhiteSpace(link.CensorStorageKey))
                    urls.Add(BuildPreviewUrl(link.MediaAssetId));
                continue;
            }

            urls.Add(string.IsNullOrWhiteSpace(link.MediaAsset.ExternalUrl)
                ? BuildPublicUrl(link.MediaAsset.Id)
                : link.MediaAsset.ExternalUrl!);
        }

        return urls;
    }

    public async Task<HubMediaPreview?> GetHubPreviewForPostAsync(Guid postId, CancellationToken ct = default)
    {
        var link = await _db.PostMedia.AsNoTracking()
            .Include(pm => pm.MediaAsset)
            .Where(pm => pm.PostId == postId)
            .OrderBy(pm => pm.SortOrder)
            .FirstOrDefaultAsync(ct);
        if (link?.MediaAsset is null) return null;

        if (link.IsNsfw)
        {
            var previewUrl = string.IsNullOrWhiteSpace(link.CensorStorageKey)
                ? null
                : BuildPreviewUrl(link.MediaAssetId);
            return new HubMediaPreview(previewUrl, link.CoverText);
        }

        var url = string.IsNullOrWhiteSpace(link.MediaAsset.ExternalUrl)
            ? BuildPublicUrl(link.MediaAsset.Id)
            : link.MediaAsset.ExternalUrl;
        return new HubMediaPreview(url, null);
    }

    public async Task SetNsfwAsync(
        Guid actorUserId,
        Guid postId,
        Guid mediaAssetId,
        bool isNsfw,
        string? coverText,
        CancellationToken ct = default)
    {
        var link = await _db.PostMedia
            .Include(pm => pm.Post)
            .Include(pm => pm.MediaAsset)
            .FirstOrDefaultAsync(pm => pm.PostId == postId && pm.MediaAssetId == mediaAssetId, ct)
            ?? throw new InvalidOperationException("Media is not attached to this post.");

        var post = link.Post ?? throw new InvalidOperationException("Post not found.");
        if (!post.ForumId.HasValue || post.WatercoolerId.HasValue)
            throw new InvalidOperationException("NSFW marks apply to forum post images.");
        if (link.MediaAsset is null)
            throw new InvalidOperationException("Media asset was not found.");

        var isAuthor = post.AuthorUserId == actorUserId;
        var perms = await _permissions.GetForumPermissionsAsync(actorUserId, post.ForumId.Value, ct);
        if (!isAuthor && !perms.HasFlag(ForumPermissions.MarkNsfwMedia))
            throw new PermissionDeniedException("MarkNsfwMedia", "forum");

        if (isNsfw)
        {
            link.IsNsfw = true;
            link.CoverText = NsfwMedia.RequireCover(coverText);
            await EnsureCensorAsync(link, link.MediaAsset, ct);
        }
        else
        {
            await ClearCensorAsync(link, ct);
            link.IsNsfw = false;
            link.CoverText = null;
        }

        await _db.SaveChangesAsync(ct);
    }

    public string BuildPublicUrl(Guid mediaId) => $"{_publicBaseUrl}/api/media/{mediaId}";

    public string BuildPreviewUrl(Guid mediaId) => $"{_publicBaseUrl}/api/media/{mediaId}/preview";

    private MediaAttachmentDto ToDto(MediaAsset asset, PostMedia? link, bool canViewOriginal)
    {
        var isNsfw = link?.IsNsfw == true;
        string? url = null;
        if (!isNsfw || canViewOriginal)
        {
            url = string.IsNullOrWhiteSpace(asset.ExternalUrl)
                ? BuildPublicUrl(asset.Id)
                : asset.ExternalUrl;
        }

        string? censorUrl = isNsfw && !string.IsNullOrWhiteSpace(link?.CensorStorageKey)
            ? BuildPreviewUrl(asset.Id)
            : null;

        return new MediaAttachmentDto(
            asset.Id,
            url,
            asset.ContentType,
            asset.ByteSize,
            asset.Width,
            asset.Height,
            isNsfw,
            isNsfw ? link?.CoverText : null,
            censorUrl);
    }

    private async Task<bool> CanAccessOriginalAsync(
        Guid? viewerUserId,
        PostMedia link,
        MediaAsset asset,
        CancellationToken ct)
    {
        if (!link.IsNsfw) return true;
        if (viewerUserId.HasValue &&
            (viewerUserId.Value == link.Post?.AuthorUserId || viewerUserId.Value == asset.UploaderUserId))
            return true;

        if (link.Post?.ForumId is not { } forumId) return false;
        var perms = await _permissions.GetForumPermissionsAsync(viewerUserId, forumId, ct);
        return perms.HasFlag(ForumPermissions.ViewNsfwMedia);
    }

    private static bool CanAccessOriginal(
        Guid? viewerUserId,
        PostMedia link,
        MediaAsset asset,
        HashSet<Guid> viewNsfwForums)
    {
        if (!link.IsNsfw) return true;
        if (viewerUserId.HasValue &&
            (viewerUserId.Value == link.Post?.AuthorUserId || viewerUserId.Value == asset.UploaderUserId))
            return true;
        return link.Post?.ForumId is { } forumId && viewNsfwForums.Contains(forumId);
    }

    private async Task EnsureCensorAsync(PostMedia link, MediaAsset asset, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(link.CensorStorageKey)) return;
        if (string.IsNullOrWhiteSpace(asset.StorageKey) || !string.IsNullOrWhiteSpace(asset.ExternalUrl))
            return;

        var original = await _storage.GetAsync(asset.StorageKey, ct);
        if (original is null) return;

        await using var stream = original.Value.Stream;
        var bytes = NsfwCensorPreview.TryCreate(stream);
        if (bytes is null || bytes.Length == 0) return;

        var key = $"{asset.UploaderUserId:N}/{asset.Id:N}.nsfw.jpg";
        await using var output = new MemoryStream(bytes, writable: false);
        await _storage.PutAsync(key, output, "image/jpeg", bytes.Length, ct);
        link.CensorStorageKey = key;
        link.CensorContentType = "image/jpeg";
        link.CensorByteSize = bytes.Length;
    }

    private async Task ClearCensorAsync(PostMedia link, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(link.CensorStorageKey))
            await _storage.DeleteAsync(link.CensorStorageKey, ct);
        link.CensorStorageKey = null;
        link.CensorContentType = null;
        link.CensorByteSize = null;
    }

    private static string NormalizeContentType(string contentType, string? fileName)
    {
        var type = (contentType ?? string.Empty).Split(';', 2)[0].Trim();
        if (!string.IsNullOrWhiteSpace(type) && !string.Equals(type, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            return type;

        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => type
        };
    }

    private static string ExtensionFor(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => ".bin"
    };

    private static bool LooksLikeImage(Stream stream, string contentType)
    {
        Span<byte> header = stackalloc byte[12];
        var read = stream.Read(header);
        if (read < 3) return false;

        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            "image/png" => read >= 8 &&
                           header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                           header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A,
            "image/gif" => read >= 6 &&
                           header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F' &&
                           header[3] == (byte)'8' && (header[4] == (byte)'7' || header[4] == (byte)'9') && header[5] == (byte)'a',
            "image/webp" => read >= 12 &&
                            header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' &&
                            header[3] == (byte)'F' &&
                            header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P',
            _ => false
        };
    }
}
