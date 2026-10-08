using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Domain.ForumPages;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IForumPageService
{
    Task<ForumPageDto> EnsureDefaultHomeAsync(Guid forumId, CancellationToken ct = default);
    Task<ForumPageDto> GetDefaultHomeAsync(Guid forumId, Guid? viewerUserId, CancellationToken ct = default);
    Task<ForumPageDto> UpdateDefaultHomeLayoutAsync(
        Guid forumId,
        Guid userId,
        UpdateForumHomeLayoutRequest request,
        CancellationToken ct = default);
    Task<ForumPageDto> EnsureChromeAsync(Guid forumId, CancellationToken ct = default);
    Task<ForumPageDto> GetChromeAsync(Guid forumId, Guid? viewerUserId, CancellationToken ct = default);
    Task<ForumPageDto> UpdateChromeLayoutAsync(
        Guid forumId,
        Guid userId,
        UpdateForumChromeLayoutRequest request,
        CancellationToken ct = default);
    Task<IReadOnlyList<ForumPageSummaryDto>> ListPagesAsync(
        Guid forumId,
        Guid? viewerUserId,
        CancellationToken ct = default);
    Task<ForumPageDto> GetPageAsync(
        Guid forumId,
        string pageSlug,
        Guid? viewerUserId,
        CancellationToken ct = default);
    Task<ForumPageDto> CreatePageAsync(
        Guid forumId,
        Guid userId,
        CreateForumPageRequest request,
        CancellationToken ct = default);
    Task<ForumPageDto> UpdatePageLayoutAsync(
        Guid forumId,
        string pageSlug,
        Guid userId,
        UpdateForumPageLayoutRequest request,
        CancellationToken ct = default);
    Task SyncPinnedWatercoolerAsync(Guid forumId, Guid? watercoolerId, CancellationToken ct = default);
}

public class ForumPageService : IForumPageService
{
    private readonly SneklDbContext _db;
    private readonly IPermissionService _permissions;

    public ForumPageService(SneklDbContext db, IPermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<ForumPageDto> EnsureDefaultHomeAsync(Guid forumId, CancellationToken ct = default)
    {
        var page = await _db.ForumPages.FirstOrDefaultAsync(p => p.ForumId == forumId && p.IsDefault, ct);
        if (page is null)
        {
            var pinnedId = await ReadPinnedCoolerIdAsync(forumId, ct);
            var root = ForumPageLayout.CreateDefaultHomeSeed(pinnedId);
            page = new ForumPage
            {
                ForumId = forumId,
                Title = "Home",
                Slug = ForumPageSlugs.Home,
                IsDefault = true,
                IsChrome = false,
                LayoutJson = ForumPageLayout.Serialize(root)
            };
            _db.ForumPages.Add(page);
            await _db.SaveChangesAsync(ct);
            return ToDto(page, root);
        }

        var layout = ForumPageLayout.ParseOrSeed(page.LayoutJson, await ReadPinnedCoolerIdAsync(forumId, ct));
        var pinnedFromBlocks = await ReadPinnedCoolerIdAsync(forumId, ct);
        if (pinnedFromBlocks is Guid pin
            && ForumPageLayout.FindPinnedWatercoolerId(layout) is null)
        {
            layout = ForumPageLayout.SyncPinnedWatercooler(layout, pin);
            page.LayoutJson = ForumPageLayout.Serialize(layout);
            page.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        else if (string.IsNullOrWhiteSpace(page.LayoutJson) || page.LayoutJson.Trim() == "{}")
        {
            page.LayoutJson = ForumPageLayout.Serialize(layout);
            page.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return ToDto(page, layout);
    }

    public async Task<ForumPageDto> GetDefaultHomeAsync(
        Guid forumId,
        Guid? viewerUserId,
        CancellationToken ct = default)
    {
        if (!await _permissions.CanSeeForumAsync(viewerUserId, forumId, ct))
            throw new PermissionDeniedException(nameof(ForumPermissions.SeeForum), "forum");
        return await EnsureDefaultHomeAsync(forumId, ct);
    }

    public async Task<ForumPageDto> UpdateDefaultHomeLayoutAsync(
        Guid forumId,
        Guid userId,
        UpdateForumHomeLayoutRequest request,
        CancellationToken ct = default)
    {
        await _permissions.RequireForumPermissionAsync(
            userId, forumId, ForumPermissions.ManageContentBlocks, ct);

        var root = ForumPageLayout.FromDto(request.Layout);
        ForumPageLayout.Validate(root);

        var page = await _db.ForumPages.FirstOrDefaultAsync(p => p.ForumId == forumId && p.IsDefault, ct);
        if (page is null)
        {
            page = new ForumPage
            {
                ForumId = forumId,
                Title = "Home",
                Slug = ForumPageSlugs.Home,
                IsDefault = true,
                IsChrome = false,
            };
            _db.ForumPages.Add(page);
        }

        page.LayoutJson = ForumPageLayout.Serialize(root);
        page.UpdatedAt = DateTime.UtcNow;
        await SyncContentBlocksFromLayoutAsync(forumId, root, ct);
        await _db.SaveChangesAsync(ct);
        return ToDto(page, root);
    }

    public async Task<ForumPageDto> EnsureChromeAsync(Guid forumId, CancellationToken ct = default)
    {
        var chrome = await _db.ForumPages.FirstOrDefaultAsync(p => p.ForumId == forumId && p.IsChrome, ct);
        if (chrome is null)
        {
            var root = ForumPageLayout.CreateDefaultChromeSeed();
            chrome = new ForumPage
            {
                ForumId = forumId,
                Title = "Shared",
                Slug = ForumPageSlugs.Chrome,
                IsDefault = false,
                IsChrome = true,
                LayoutJson = ForumPageLayout.Serialize(root)
            };
            _db.ForumPages.Add(chrome);
            await MigrateHomeHeadersIntoChromeAsync(forumId, ct);
            await _db.SaveChangesAsync(ct);
            return ToChromeDto(chrome, root);
        }

        var layout = ForumPageLayout.ParseChromeOrSeed(chrome.LayoutJson);
        if (string.IsNullOrWhiteSpace(chrome.LayoutJson) || chrome.LayoutJson.Trim() == "{}")
        {
            chrome.LayoutJson = ForumPageLayout.Serialize(layout);
            chrome.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return ToChromeDto(chrome, layout);
    }

    public async Task<ForumPageDto> GetChromeAsync(
        Guid forumId,
        Guid? viewerUserId,
        CancellationToken ct = default)
    {
        if (!await _permissions.CanSeeForumAsync(viewerUserId, forumId, ct))
            throw new PermissionDeniedException(nameof(ForumPermissions.SeeForum), "forum");
        return await EnsureChromeAsync(forumId, ct);
    }

    public async Task<ForumPageDto> UpdateChromeLayoutAsync(
        Guid forumId,
        Guid userId,
        UpdateForumChromeLayoutRequest request,
        CancellationToken ct = default)
    {
        await _permissions.RequireForumPermissionAsync(
            userId, forumId, ForumPermissions.ManageContentBlocks, ct);

        var root = ForumPageLayout.FromDto(request.Layout);
        ForumPageLayout.ValidateChrome(root);

        var chrome = await _db.ForumPages.FirstOrDefaultAsync(p => p.ForumId == forumId && p.IsChrome, ct);
        if (chrome is null)
        {
            chrome = new ForumPage
            {
                ForumId = forumId,
                Title = "Shared",
                Slug = ForumPageSlugs.Chrome,
                IsDefault = false,
                IsChrome = true,
            };
            _db.ForumPages.Add(chrome);
        }

        chrome.LayoutJson = ForumPageLayout.Serialize(root);
        chrome.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToChromeDto(chrome, root);
    }

    public async Task<IReadOnlyList<ForumPageSummaryDto>> ListPagesAsync(
        Guid forumId,
        Guid? viewerUserId,
        CancellationToken ct = default)
    {
        if (!await _permissions.CanSeeForumAsync(viewerUserId, forumId, ct))
            throw new PermissionDeniedException(nameof(ForumPermissions.SeeForum), "forum");

        await EnsureDefaultHomeAsync(forumId, ct);
        return await _db.ForumPages.AsNoTracking()
            .Where(p => p.ForumId == forumId && !p.IsChrome)
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Title)
            .Select(p => new ForumPageSummaryDto(p.Id, p.Title, p.Slug, p.IsDefault))
            .ToListAsync(ct);
    }

    public async Task<ForumPageDto> GetPageAsync(
        Guid forumId,
        string pageSlug,
        Guid? viewerUserId,
        CancellationToken ct = default)
    {
        if (!await _permissions.CanSeeForumAsync(viewerUserId, forumId, ct))
            throw new PermissionDeniedException(nameof(ForumPermissions.SeeForum), "forum");

        var slug = NormalizeSlug(pageSlug);
        if (string.Equals(slug, ForumPageSlugs.Home, StringComparison.OrdinalIgnoreCase))
            return await EnsureDefaultHomeAsync(forumId, ct);
        if (string.Equals(slug, ForumPageSlugs.Chrome, StringComparison.OrdinalIgnoreCase))
            return await EnsureChromeAsync(forumId, ct);

        var page = await _db.ForumPages.FirstOrDefaultAsync(
            p => p.ForumId == forumId && p.Slug == slug && !p.IsChrome, ct)
            ?? throw new KeyNotFoundException("Page not found.");

        var root = ForumPageLayout.ParseOrSeed(page.LayoutJson);
        return ToDto(page, root);
    }

    public async Task<ForumPageDto> CreatePageAsync(
        Guid forumId,
        Guid userId,
        CreateForumPageRequest request,
        CancellationToken ct = default)
    {
        await _permissions.RequireForumPermissionAsync(
            userId, forumId, ForumPermissions.ManageContentBlocks, ct);

        var title = (request.Title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Page title is required.");
        if (title.Length > 200)
            throw new InvalidOperationException("Page title must be 200 characters or fewer.");

        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? Slugify(title)
            : NormalizeSlug(request.Slug);
        if (string.IsNullOrWhiteSpace(slug))
            throw new InvalidOperationException("Page slug is required.");
        if (ForumPageSlugs.Reserved.Contains(slug))
            throw new InvalidOperationException($"Slug '{slug}' is reserved.");

        var taken = await _db.ForumPages.AnyAsync(p => p.ForumId == forumId && p.Slug == slug, ct);
        if (taken)
            throw new InvalidOperationException($"A page with slug '{slug}' already exists.");

        var root = ForumPageLayout.CreateDefaultExtraPageSeed();
        var page = new ForumPage
        {
            ForumId = forumId,
            Title = title,
            Slug = slug,
            IsDefault = false,
            IsChrome = false,
            LayoutJson = ForumPageLayout.Serialize(root)
        };
        _db.ForumPages.Add(page);
        await _db.SaveChangesAsync(ct);
        return ToDto(page, root);
    }

    public async Task<ForumPageDto> UpdatePageLayoutAsync(
        Guid forumId,
        string pageSlug,
        Guid userId,
        UpdateForumPageLayoutRequest request,
        CancellationToken ct = default)
    {
        await _permissions.RequireForumPermissionAsync(
            userId, forumId, ForumPermissions.ManageContentBlocks, ct);

        var slug = NormalizeSlug(pageSlug);
        if (string.Equals(slug, ForumPageSlugs.Home, StringComparison.OrdinalIgnoreCase))
            return await UpdateDefaultHomeLayoutAsync(
                forumId, userId, new UpdateForumHomeLayoutRequest(request.Layout), ct);
        if (string.Equals(slug, ForumPageSlugs.Chrome, StringComparison.OrdinalIgnoreCase)
            || ForumPageSlugs.Reserved.Contains(slug))
            throw new InvalidOperationException($"Slug '{slug}' cannot be edited as a normal page.");

        var root = ForumPageLayout.FromDto(request.Layout);
        ForumPageLayout.Validate(root);

        var page = await _db.ForumPages.FirstOrDefaultAsync(
            p => p.ForumId == forumId && p.Slug == slug && !p.IsChrome && !p.IsDefault, ct)
            ?? throw new KeyNotFoundException("Page not found.");

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            var title = request.Title.Trim();
            if (title.Length > 200)
                throw new InvalidOperationException("Page title must be 200 characters or fewer.");
            page.Title = title;
        }

        if (request.Slug is not null)
        {
            var nextSlug = NormalizeSlug(request.Slug);
            if (string.IsNullOrWhiteSpace(nextSlug))
                throw new InvalidOperationException("Page slug is required.");
            if (!string.Equals(nextSlug, page.Slug, StringComparison.Ordinal))
            {
                if (ForumPageSlugs.Reserved.Contains(nextSlug))
                    throw new InvalidOperationException($"Slug '{nextSlug}' is reserved.");
                var taken = await _db.ForumPages.AnyAsync(
                    p => p.ForumId == forumId && p.Slug == nextSlug, ct);
                if (taken)
                    throw new InvalidOperationException($"A page with slug '{nextSlug}' already exists.");
                page.Slug = nextSlug;
            }
        }

        page.LayoutJson = ForumPageLayout.Serialize(root);
        page.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDto(page, root);
    }

    public async Task SyncPinnedWatercoolerAsync(Guid forumId, Guid? watercoolerId, CancellationToken ct = default)
    {
        var home = await EnsureDefaultHomeAsync(forumId, ct);
        var page = await _db.ForumPages.FirstAsync(p => p.Id == home.Id, ct);
        var root = ForumPageLayout.Parse(page.LayoutJson);
        root = ForumPageLayout.SyncPinnedWatercooler(root, watercoolerId);
        ForumPageLayout.Validate(root);
        page.LayoutJson = ForumPageLayout.Serialize(root);
        page.UpdatedAt = DateTime.UtcNow;
        await SyncContentBlocksFromLayoutAsync(forumId, root, ct);
        await _db.SaveChangesAsync(ct);
    }

    private async Task MigrateHomeHeadersIntoChromeAsync(Guid forumId, CancellationToken ct)
    {
        var home = await _db.ForumPages.FirstOrDefaultAsync(p => p.ForumId == forumId && p.IsDefault, ct);
        if (home is null) return;
        try
        {
            var layout = ForumPageLayout.Parse(home.LayoutJson);
            var stripped = ForumPageLayout.StripTopLevelForumHeaders(layout);
            ForumPageLayout.Validate(stripped);
            home.LayoutJson = ForumPageLayout.Serialize(stripped);
            home.UpdatedAt = DateTime.UtcNow;
        }
        catch
        {
            // Leave home as-is if corrupt; EnsureDefaultHome will reseed later.
        }
    }

    private async Task SyncContentBlocksFromLayoutAsync(Guid forumId, ForumPageRegion root, CancellationToken ct)
    {
        var existing = await _db.ForumContentBlocks.Where(b => b.ForumId == forumId).ToListAsync(ct);
        _db.ForumContentBlocks.RemoveRange(existing);
        var pinned = ForumPageLayout.FindPinnedWatercoolerId(root);
        if (pinned is Guid coolerId)
        {
            _db.ForumContentBlocks.Add(new ForumContentBlock
            {
                ForumId = forumId,
                TypeKey = WatercoolerBlockTypes.CoolerFeed,
                WatercoolerId = coolerId,
                Slot = ContentBlockSlots.Main,
                Position = 0
            });
        }
    }

    private async Task<Guid?> ReadPinnedCoolerIdAsync(Guid forumId, CancellationToken ct)
    {
        return await _db.ForumContentBlocks.AsNoTracking()
            .Where(b => b.ForumId == forumId
                && b.TypeKey == WatercoolerBlockTypes.CoolerFeed
                && b.Slot == ContentBlockSlots.Main
                && b.WatercoolerId != null)
            .OrderBy(b => b.Position)
            .Select(b => b.WatercoolerId)
            .FirstOrDefaultAsync(ct);
    }

    public static ForumPageDto ToDto(ForumPage page, ForumPageRegion root) =>
        new(
            page.Id,
            page.Title,
            page.Slug,
            page.IsDefault,
            ToRegionDto(root),
            ForumPageLayout.SoftGuidance(root),
            page.IsChrome);

    public static ForumPageDto ToChromeDto(ForumPage page, ForumPageRegion root) =>
        new(
            page.Id,
            page.Title,
            page.Slug,
            page.IsDefault,
            ToRegionDto(root),
            ForumPageLayout.SoftGuidanceChrome(root),
            IsChrome: true);

    public static ForumPageRegionDto ToRegionDto(ForumPageRegion region) =>
        new(
            region.Id,
            region.TypeKey,
            region.Settings,
            region.Children?.Select(ToRegionDto).ToList());

    private static string NormalizeSlug(string? slug)
    {
        var value = (slug ?? "").Trim().ToLowerInvariant();
        value = System.Text.RegularExpressions.Regex.Replace(value, @"[^a-z0-9\-]+", "-");
        value = System.Text.RegularExpressions.Regex.Replace(value, @"-+", "-").Trim('-');
        return value.Length > 64 ? value[..64].TrimEnd('-') : value;
    }

    private static string Slugify(string title) => NormalizeSlug(title);
}
