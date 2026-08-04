using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface ICuratedListService
{
    Task<CuratedListDto> CreateAsync(Guid ownerId, CreateCuratedListRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<CuratedListDto>> ListAsync(Guid? viewerId, CancellationToken ct = default);
    Task<CuratedListDto?> GetAsync(Guid listId, Guid? viewerId, CancellationToken ct = default);
    Task<ListAnnotationDto> AddEntryAsync(Guid listId, Guid actorId, AddListEntryRequest request, CancellationToken ct = default);
    Task SubscribeAsync(Guid listId, Guid userId, SubscribeListRequest request, CancellationToken ct = default);
    Task UnsubscribeAsync(Guid listId, Guid userId, CancellationToken ct = default);
}

public class CuratedListService : ICuratedListService
{
    private readonly SneklDbContext _db;

    public CuratedListService(SneklDbContext db) => _db = db;

    public async Task<CuratedListDto> CreateAsync(Guid ownerId, CreateCuratedListRequest request, CancellationToken ct = default)
    {
        if (!Enum.TryParse<ListBehavior>(request.DefaultBehavior, true, out var behavior))
            throw new InvalidOperationException("Invalid list behavior.");

        var list = new CuratedList
        {
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            OwnerUserId = ownerId,
            DefaultBehavior = behavior,
            DefaultFlairEmoji = request.DefaultFlairEmoji,
            DefaultIconUrl = request.DefaultIconUrl,
            IsPublic = request.IsPublic
        };

        _db.CuratedLists.Add(list);
        await _db.SaveChangesAsync(ct);
        return ToDto(list, ownerId, 0, 0);
    }

    public async Task<IReadOnlyList<CuratedListDto>> ListAsync(Guid? viewerId, CancellationToken ct = default)
    {
        var lists = await _db.CuratedLists
            .Where(l => l.IsPublic || (viewerId.HasValue && l.OwnerUserId == viewerId.Value))
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        return await Task.WhenAll(lists.Select(async l =>
        {
            var entryCount = await _db.ListEntries.CountAsync(e => e.ListId == l.Id, ct);
            var subCount = await _db.ListSubscriptions.CountAsync(s => s.ListId == l.Id, ct);
            return ToDto(l, viewerId ?? Guid.Empty, entryCount, subCount);
        }));
    }

    public async Task<CuratedListDto?> GetAsync(Guid listId, Guid? viewerId, CancellationToken ct = default)
    {
        var list = await _db.CuratedLists.FindAsync([listId], ct);
        if (list is null || (!list.IsPublic && viewerId != list.OwnerUserId)) return null;

        var entryCount = await _db.ListEntries.CountAsync(e => e.ListId == listId, ct);
        var subCount = await _db.ListSubscriptions.CountAsync(s => s.ListId == listId, ct);
        return ToDto(list, viewerId ?? Guid.Empty, entryCount, subCount);
    }

    public async Task<ListAnnotationDto> AddEntryAsync(Guid listId, Guid actorId, AddListEntryRequest request, CancellationToken ct = default)
    {
        var list = await _db.CuratedLists.FindAsync([listId], ct)
            ?? throw new InvalidOperationException("List not found.");

        if (list.OwnerUserId != actorId)
            throw new InvalidOperationException("Only the list owner can add entries.");

        if (await _db.ListEntries.AnyAsync(e => e.ListId == listId && e.TargetUserId == request.TargetUserId, ct))
            throw new InvalidOperationException("User is already on this list.");

        var overlay = request.AvatarOverlay;
        OverlayKind? overlayKind = overlay is not null && Enum.TryParse<OverlayKind>(overlay.Kind, true, out var kind)
            ? kind : null;

        var label = request.Label.Trim();
        if (string.IsNullOrWhiteSpace(label))
            throw new InvalidOperationException("Label is required.");

        var entry = new ListEntry
        {
            ListId = listId,
            TargetUserId = request.TargetUserId,
            Label = label,
            Severity = SeverityForBehavior(list.DefaultBehavior),
            TooltipText = request.TooltipText.Trim(),
            FlairEmoji = request.FlairEmoji,
            FlairIconUrl = request.FlairIconUrl,
            UsernameColor = request.UsernameColor,
            UsernameStyle = request.UsernameStyle,
            OverlayKind = overlayKind,
            OverlayColor = overlay?.Color,
            OverlayEmoji = overlay?.Emoji,
            OverlayIconUrl = overlay?.IconUrl,
            OverlayAriaLabel = overlay?.AriaLabel,
            AddedByUserId = actorId
        };

        _db.ListEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        return PresentationMapper.ToAnnotationDto(entry, list);
    }

    public async Task SubscribeAsync(Guid listId, Guid userId, SubscribeListRequest request, CancellationToken ct = default)
    {
        if (!await _db.CuratedLists.AnyAsync(l => l.Id == listId, ct))
            throw new InvalidOperationException("List not found.");

        if (await _db.ListSubscriptions.AnyAsync(s => s.ListId == listId && s.UserId == userId, ct))
            throw new InvalidOperationException("Already subscribed.");

        if (!Enum.TryParse<ListBehavior>(request.AppliedBehavior, true, out var behavior))
            throw new InvalidOperationException("Invalid behavior.");

        _db.ListSubscriptions.Add(new ListSubscription
        {
            ListId = listId,
            UserId = userId,
            AppliedBehavior = behavior
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task UnsubscribeAsync(Guid listId, Guid userId, CancellationToken ct = default)
    {
        var sub = await _db.ListSubscriptions.FirstOrDefaultAsync(s => s.ListId == listId && s.UserId == userId, ct);
        if (sub is null) return;
        _db.ListSubscriptions.Remove(sub);
        await _db.SaveChangesAsync(ct);
    }

    private static AnnotationSeverity SeverityForBehavior(ListBehavior behavior) => behavior switch
    {
        ListBehavior.Verify or ListBehavior.Highlight => AnnotationSeverity.Highlight,
        ListBehavior.Warn => AnnotationSeverity.Warn,
        ListBehavior.Hide or ListBehavior.Mute => AnnotationSeverity.Restrict,
        _ => AnnotationSeverity.Notice
    };

    private static CuratedListDto ToDto(CuratedList list, Guid viewerId, int entryCount, int subCount) => new(
        list.Id,
        list.Name,
        list.Description,
        list.DefaultBehavior.ToString(),
        list.DefaultFlairEmoji,
        list.DefaultIconUrl,
        list.IsPublic,
        entryCount,
        subCount,
        list.CreatedAt,
        list.OwnerUserId == viewerId);
}
