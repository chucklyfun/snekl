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
    Task<CuratedListDto> UpdateAsync(Guid listId, Guid actorId, UpdateCuratedListRequest request, CancellationToken ct = default);
    Task<CuratedListDto> TransferOwnershipAsync(Guid listId, Guid actorId, string username, CancellationToken ct = default);
    Task DeleteAsync(Guid listId, Guid actorId, CancellationToken ct = default);
    Task<PagedResult<ListEntryDto>> ListEntriesAsync(
        Guid listId,
        Guid? viewerId,
        int page = 1,
        int pageSize = 50,
        string? q = null,
        CancellationToken ct = default);
    Task<ListEntryDto> AddEntryAsync(Guid listId, Guid actorId, AddListEntryRequest request, CancellationToken ct = default);
    Task<ListEntryDto> UpdateEntryAsync(Guid listId, Guid entryId, Guid actorId, UpdateListEntryRequest request, CancellationToken ct = default);
    Task DeleteEntryAsync(Guid listId, Guid entryId, Guid actorId, CancellationToken ct = default);
    Task SubscribeAsync(Guid listId, Guid userId, SubscribeListRequest request, CancellationToken ct = default);
    Task UnsubscribeAsync(Guid listId, Guid userId, CancellationToken ct = default);
    Task ReorderSubscriptionsAsync(Guid userId, IReadOnlyList<Guid> listIds, CancellationToken ct = default);
    Task IgnoreEntryAsync(Guid listId, Guid entryId, Guid userId, CancellationToken ct = default);
    Task UnignoreEntryAsync(Guid listId, Guid entryId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<UserListEntryHit>> FindEntriesForUserAsync(
        string username,
        Guid? viewerId,
        CancellationToken ct = default);
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
        var priority = await NextSubscriptionPriorityAsync(ownerId, ct);
        _db.ListSubscriptions.Add(new ListSubscription
        {
            ListId = list.Id,
            UserId = ownerId,
            AppliedBehavior = behavior,
            Priority = priority
        });
        await _db.SaveChangesAsync(ct);
        var ownerUsername = await OwnerUsernameAsync(ownerId, ct);
        return ToDto(list, ownerId, 0, 1, subscribed: true, ownerUsername, priority);
    }

    public async Task<IReadOnlyList<CuratedListDto>> ListAsync(Guid? viewerId, CancellationToken ct = default)
    {
        var lists = await _db.CuratedLists
            .AsNoTracking()
            .Include(l => l.Owner)
            .Where(l => l.IsPublic || (viewerId.HasValue && l.OwnerUserId == viewerId.Value))
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        if (lists.Count == 0) return [];

        var listIds = lists.Select(l => l.Id).ToList();
        var entryCounts = await _db.ListEntries.AsNoTracking()
            .Where(e => listIds.Contains(e.ListId))
            .GroupBy(e => e.ListId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var subCounts = await _db.ListSubscriptions.AsNoTracking()
            .Where(s => listIds.Contains(s.ListId))
            .GroupBy(s => s.ListId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var viewerSubs = viewerId.HasValue
            ? await _db.ListSubscriptions.AsNoTracking()
                .Where(s => s.UserId == viewerId.Value)
                .ToDictionaryAsync(s => s.ListId, ct)
            : [];

        return lists.Select(l =>
        {
            viewerSubs.TryGetValue(l.Id, out var sub);
            return ToDto(
                l,
                viewerId ?? Guid.Empty,
                entryCounts.GetValueOrDefault(l.Id),
                subCounts.GetValueOrDefault(l.Id),
                sub is not null,
                l.Owner?.Username,
                sub?.Priority);
        }).ToList();
    }

    public async Task<CuratedListDto?> GetAsync(Guid listId, Guid? viewerId, CancellationToken ct = default)
    {
        var list = await _db.CuratedLists.FindAsync([listId], ct);
        if (list is null || (!list.IsPublic && viewerId != list.OwnerUserId)) return null;

        var entryCount = await _db.ListEntries.CountAsync(e => e.ListId == listId, ct);
        var subCount = await _db.ListSubscriptions.CountAsync(s => s.ListId == listId, ct);
        var sub = viewerId.HasValue
            ? await _db.ListSubscriptions.FirstOrDefaultAsync(s => s.ListId == listId && s.UserId == viewerId.Value, ct)
            : null;
        var ownerUsername = await OwnerUsernameAsync(list.OwnerUserId, ct);
        return ToDto(list, viewerId ?? Guid.Empty, entryCount, subCount, sub is not null, ownerUsername, sub?.Priority);
    }

    public async Task<CuratedListDto> UpdateAsync(Guid listId, Guid actorId, UpdateCuratedListRequest request, CancellationToken ct = default)
    {
        var list = await RequireOwnedListAsync(listId, actorId, ct);
        if (!Enum.TryParse<ListBehavior>(request.DefaultBehavior, true, out var behavior))
            throw new InvalidOperationException("Invalid list behavior.");

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Name is required.");

        list.Name = name;
        list.Description = request.Description.Trim();
        list.DefaultBehavior = behavior;
        list.DefaultFlairEmoji = request.DefaultFlairEmoji;
        list.DefaultIconUrl = request.DefaultIconUrl;
        list.IsPublic = request.IsPublic;
        await _db.SaveChangesAsync(ct);

        var entryCount = await _db.ListEntries.CountAsync(e => e.ListId == list.Id, ct);
        var subCount = await _db.ListSubscriptions.CountAsync(s => s.ListId == list.Id, ct);
        var sub = await _db.ListSubscriptions.FirstOrDefaultAsync(s => s.ListId == list.Id && s.UserId == actorId, ct);
        var ownerUsername = await OwnerUsernameAsync(list.OwnerUserId, ct);
        return ToDto(list, actorId, entryCount, subCount, sub is not null, ownerUsername, sub?.Priority);
    }

    public async Task<CuratedListDto> TransferOwnershipAsync(
        Guid listId,
        Guid actorId,
        string username,
        CancellationToken ct = default)
    {
        var list = await RequireOwnedListAsync(listId, actorId, ct);
        var handle = username.Trim().TrimStart('@').ToLowerInvariant();
        if (handle.Length == 0)
            throw new InvalidOperationException("Username is required.");

        var recipient = await _db.Users.FirstOrDefaultAsync(u => u.Username == handle, ct);
        if (recipient is null)
            throw new InvalidOperationException(
                string.Format(OwnershipLookup.MissingLocalAccountMessage, handle));

        if (recipient.Id == actorId)
            throw new InvalidOperationException("You already own this list.");

        list.OwnerUserId = recipient.Id;
        var recipientSub = await _db.ListSubscriptions
            .FirstOrDefaultAsync(s => s.ListId == list.Id && s.UserId == recipient.Id, ct);
        if (recipientSub is null)
        {
            _db.ListSubscriptions.Add(new ListSubscription
            {
                ListId = list.Id,
                UserId = recipient.Id,
                AppliedBehavior = list.DefaultBehavior,
                Priority = await NextSubscriptionPriorityAsync(recipient.Id, ct)
            });
        }

        await _db.SaveChangesAsync(ct);

        var entryCount = await _db.ListEntries.CountAsync(e => e.ListId == list.Id, ct);
        var subCount = await _db.ListSubscriptions.CountAsync(s => s.ListId == list.Id, ct);
        var actorSub = await _db.ListSubscriptions.FirstOrDefaultAsync(
            s => s.ListId == list.Id && s.UserId == actorId, ct);
        return ToDto(list, actorId, entryCount, subCount, actorSub is not null, recipient.Username, actorSub?.Priority);
    }

    public async Task DeleteAsync(Guid listId, Guid actorId, CancellationToken ct = default)
    {
        var list = await RequireOwnedListAsync(listId, actorId, ct);
        _db.ListEntries.RemoveRange(_db.ListEntries.Where(e => e.ListId == list.Id));
        _db.ListSubscriptions.RemoveRange(_db.ListSubscriptions.Where(s => s.ListId == list.Id));
        _db.ListEntryIgnores.RemoveRange(_db.ListEntryIgnores.Where(i => i.ListId == list.Id));
        _db.CuratedLists.Remove(list);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<ListEntryDto>> ListEntriesAsync(
        Guid listId,
        Guid? viewerId,
        int page = 1,
        int pageSize = 50,
        string? q = null,
        CancellationToken ct = default)
    {
        var list = await _db.CuratedLists.FindAsync([listId], ct)
            ?? throw new InvalidOperationException("List not found.");
        if (!list.IsPublic && viewerId != list.OwnerUserId)
            throw new InvalidOperationException("List not found.");

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.ListEntries
            .AsNoTracking()
            .Include(e => e.TargetUser)
            .Where(e => e.ListId == listId);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().TrimStart('@').ToLowerInvariant();
            query = query.Where(e =>
                e.Label.ToLower().Contains(term) ||
                (e.TargetUser != null && e.TargetUser.Username.ToLower().Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var entries = await query
            .OrderBy(e => e.TargetUser!.Username)
            .ThenBy(e => e.Label)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var ignoredIds = viewerId.HasValue
            ? (await _db.ListEntryIgnores
                .AsNoTracking()
                .Where(i => i.UserId == viewerId.Value && i.ListId == listId)
                .Select(i => i.TargetUserId)
                .ToListAsync(ct)).ToHashSet()
            : [];
        return new PagedResult<ListEntryDto>(
            entries.Select(e => ToEntryDto(e, ignoredIds.Contains(e.TargetUserId))).ToList(),
            total,
            page,
            pageSize);
    }

    public async Task<ListEntryDto> AddEntryAsync(Guid listId, Guid actorId, AddListEntryRequest request, CancellationToken ct = default)
    {
        var list = await RequireOwnedListAsync(listId, actorId, ct);

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
        await _db.Entry(entry).Reference(e => e.TargetUser).LoadAsync(ct);
        return ToEntryDto(entry);
    }

    public async Task<ListEntryDto> UpdateEntryAsync(
        Guid listId,
        Guid entryId,
        Guid actorId,
        UpdateListEntryRequest request,
        CancellationToken ct = default)
    {
        await RequireOwnedListAsync(listId, actorId, ct);
        var entry = await _db.ListEntries.Include(e => e.TargetUser)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.ListId == listId, ct)
            ?? throw new InvalidOperationException("Entry not found.");

        var label = request.Label.Trim();
        if (string.IsNullOrWhiteSpace(label))
            throw new InvalidOperationException("Label is required.");

        entry.Label = label;
        entry.TooltipText = request.TooltipText.Trim();
        entry.FlairEmoji = request.FlairEmoji;
        entry.FlairIconUrl = request.FlairIconUrl;
        await _db.SaveChangesAsync(ct);
        return ToEntryDto(entry);
    }

    public async Task DeleteEntryAsync(Guid listId, Guid entryId, Guid actorId, CancellationToken ct = default)
    {
        await RequireOwnedListAsync(listId, actorId, ct);
        var entry = await _db.ListEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.ListId == listId, ct)
            ?? throw new InvalidOperationException("Entry not found.");
        _db.ListEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SubscribeAsync(Guid listId, Guid userId, SubscribeListRequest request, CancellationToken ct = default)
    {
        var list = await _db.CuratedLists.FindAsync([listId], ct)
            ?? throw new InvalidOperationException("List not found.");
        if (!list.IsPublic && list.OwnerUserId != userId)
            throw new InvalidOperationException("List not found.");

        if (!Enum.TryParse<ListBehavior>(request.AppliedBehavior, true, out var behavior))
            throw new InvalidOperationException("Invalid behavior.");

        var existing = await _db.ListSubscriptions.FirstOrDefaultAsync(s => s.ListId == listId && s.UserId == userId, ct);
        if (existing is not null)
        {
            existing.AppliedBehavior = behavior;
            await _db.SaveChangesAsync(ct);
            return;
        }

        _db.ListSubscriptions.Add(new ListSubscription
        {
            ListId = listId,
            UserId = userId,
            AppliedBehavior = behavior,
            Priority = await NextSubscriptionPriorityAsync(userId, ct)
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

    public async Task ReorderSubscriptionsAsync(Guid userId, IReadOnlyList<Guid> listIds, CancellationToken ct = default)
    {
        var subs = await _db.ListSubscriptions.Where(s => s.UserId == userId).ToListAsync(ct);
        var rank = new Dictionary<Guid, int>();
        var next = 0;
        foreach (var listId in listIds.Distinct())
        {
            if (subs.Any(s => s.ListId == listId))
                rank[listId] = next++;
        }

        foreach (var sub in subs.OrderBy(s => s.Priority).ThenBy(s => s.SubscribedAt))
        {
            if (!rank.ContainsKey(sub.ListId))
                rank[sub.ListId] = next++;
            sub.Priority = rank[sub.ListId];
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task IgnoreEntryAsync(Guid listId, Guid entryId, Guid userId, CancellationToken ct = default)
    {
        var list = await _db.CuratedLists.FindAsync([listId], ct)
            ?? throw new InvalidOperationException("List not found.");
        if (!list.IsPublic && list.OwnerUserId != userId)
            throw new InvalidOperationException("List not found.");

        var entry = await _db.ListEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.ListId == listId, ct)
            ?? throw new InvalidOperationException("Entry not found.");

        if (await _db.ListEntryIgnores.AnyAsync(
                i => i.UserId == userId && i.ListId == listId && i.TargetUserId == entry.TargetUserId, ct))
            return;

        _db.ListEntryIgnores.Add(new ListEntryIgnore
        {
            UserId = userId,
            ListId = listId,
            TargetUserId = entry.TargetUserId
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task UnignoreEntryAsync(Guid listId, Guid entryId, Guid userId, CancellationToken ct = default)
    {
        var entry = await _db.ListEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.ListId == listId, ct)
            ?? throw new InvalidOperationException("Entry not found.");
        var ignore = await _db.ListEntryIgnores.FirstOrDefaultAsync(
            i => i.UserId == userId && i.ListId == listId && i.TargetUserId == entry.TargetUserId, ct);
        if (ignore is null) return;
        _db.ListEntryIgnores.Remove(ignore);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<UserListEntryHit>> FindEntriesForUserAsync(
        string username,
        Guid? viewerId,
        CancellationToken ct = default)
    {
        var handle = username.Trim().TrimStart('@').ToLowerInvariant();
        if (handle.Length == 0) return [];

        var target = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == handle, ct);
        if (target is null) return [];

        var entries = await _db.ListEntries
            .AsNoTracking()
            .Include(e => e.List)!.ThenInclude(l => l!.Owner)
            .Include(e => e.TargetUser)
            .Where(e => e.TargetUserId == target.Id && e.List != null &&
                (e.List.IsPublic || (viewerId.HasValue && e.List.OwnerUserId == viewerId.Value)))
            .ToListAsync(ct);

        var subs = viewerId.HasValue
            ? await _db.ListSubscriptions.Where(s => s.UserId == viewerId.Value).ToDictionaryAsync(s => s.ListId, ct)
            : [];
        var ignored = viewerId.HasValue
            ? (await _db.ListEntryIgnores
                .AsNoTracking()
                .Where(i => i.UserId == viewerId.Value && i.TargetUserId == target.Id)
                .Select(i => i.ListId)
                .ToListAsync(ct)).ToHashSet()
            : [];

        return entries
            .Select(e =>
            {
                subs.TryGetValue(e.ListId, out var sub);
                var isIgnored = ignored.Contains(e.ListId);
                return new UserListEntryHit(
                    e.Id,
                    e.ListId,
                    e.List!.Name,
                    e.List.Owner?.Username,
                    e.TargetUser?.Username ?? target.Username,
                    e.Label,
                    (int)e.Severity,
                    e.TooltipText,
                    e.FlairEmoji ?? e.List.DefaultFlairEmoji,
                    e.FlairIconUrl ?? e.List.DefaultIconUrl,
                    sub is not null,
                    sub?.Priority,
                    isIgnored,
                    sub is not null && !isIgnored,
                    e.List.DefaultBehavior.ToString());
            })
            .OrderBy(h => h.IsViewerSubscribed ? 0 : 1)
            .ThenBy(h => h.ViewerPriority ?? int.MaxValue)
            .ThenBy(h => h.ListName)
            .ToList();
    }

    private async Task<CuratedList> RequireOwnedListAsync(Guid listId, Guid actorId, CancellationToken ct)
    {
        var list = await _db.CuratedLists.FindAsync([listId], ct)
            ?? throw new InvalidOperationException("List not found.");
        if (list.OwnerUserId != actorId)
            throw new InvalidOperationException("Only the list owner can change this list.");
        return list;
    }

    private static AnnotationSeverity SeverityForBehavior(ListBehavior behavior) => behavior switch
    {
        ListBehavior.Verify or ListBehavior.Highlight => AnnotationSeverity.Highlight,
        ListBehavior.Warn => AnnotationSeverity.Warn,
        ListBehavior.Hide or ListBehavior.Mute => AnnotationSeverity.Restrict,
        _ => AnnotationSeverity.Notice
    };

    private async Task<string?> OwnerUsernameAsync(Guid ownerUserId, CancellationToken ct) =>
        await _db.Users.AsNoTracking()
            .Where(u => u.Id == ownerUserId)
            .Select(u => u.Username)
            .FirstOrDefaultAsync(ct);

    private async Task<int> NextSubscriptionPriorityAsync(Guid userId, CancellationToken ct)
    {
        var max = await _db.ListSubscriptions
            .Where(s => s.UserId == userId)
            .Select(s => (int?)s.Priority)
            .MaxAsync(ct);
        return (max ?? -1) + 1;
    }

    private static CuratedListDto ToDto(
        CuratedList list,
        Guid viewerId,
        int entryCount,
        int subCount,
        bool subscribed,
        string? ownerUsername,
        int? viewerPriority) => new(
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
        list.OwnerUserId == viewerId,
        subscribed,
        ownerUsername,
        viewerPriority);

    private static ListEntryDto ToEntryDto(ListEntry entry, bool ignored = false) => new(
        entry.Id,
        entry.ListId,
        entry.TargetUserId,
        entry.TargetUser?.Username ?? string.Empty,
        entry.Label,
        (int)entry.Severity,
        entry.TooltipText,
        entry.FlairEmoji,
        entry.FlairIconUrl,
        entry.AddedAt,
        ignored);
}
