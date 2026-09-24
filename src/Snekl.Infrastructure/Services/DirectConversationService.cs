using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IDirectConversationService
{
    Task<DirectMessageEligibilityDto> GetEligibilityAsync(
        Guid viewerUserId,
        string targetUsername,
        CancellationToken ct = default);

    Task<DirectConversationRequestDto> CreateRequestAsync(
        Guid requesterUserId,
        CreateDirectConversationRequestDto request,
        CancellationToken ct = default);

    Task<DirectConversationRequestDto> AcceptAsync(Guid targetUserId, Guid requestId, CancellationToken ct = default);
    Task<DirectConversationRequestDto> DeclineAsync(Guid targetUserId, Guid requestId, CancellationToken ct = default);
    Task CancelAsync(Guid requesterUserId, Guid requestId, CancellationToken ct = default);

    Task<DirectMessageInboxDto> GetInboxAsync(Guid userId, CancellationToken ct = default);
    Task<DirectConversationDto> GetConversationAsync(Guid userId, Guid conversationId, CancellationToken ct = default);
    Task<DirectMessageDto> SendMessageAsync(Guid userId, Guid conversationId, string body, CancellationToken ct = default);
    Task<DirectMessageForumPreferencesDto> ListForumPreferencesAsync(Guid userId, CancellationToken ct = default);
    Task<DirectMessageForumPreferenceDto> SetForumPreferenceAsync(
        Guid userId,
        Guid forumId,
        string mode,
        CancellationToken ct = default);
}

public class DirectConversationService : IDirectConversationService
{
    private const int MaxRequestMessageLength = 2000;
    private const int MaxDirectMessageLength = 8000;

    private readonly SneklDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly IForumModerationService _moderation;

    public DirectConversationService(
        SneklDbContext db,
        IPermissionService permissions,
        IForumModerationService moderation)
    {
        _db = db;
        _permissions = permissions;
        _moderation = moderation;
    }

    public async Task<DirectMessageEligibilityDto> GetEligibilityAsync(
        Guid viewerUserId,
        string targetUsername,
        CancellationToken ct = default)
    {
        var target = await FindUserByUsernameAsync(targetUsername, ct);
        var pending = await FindPendingBetweenAsync(viewerUserId, target.Id, ct);
        var conversation = await FindConversationAsync(viewerUserId, target.Id, ct);
        var permittedForums = await ListEligibleForumsAsync(viewerUserId, target.Id, ct);
        var blockedForumIds = await LoadBlockedForumIdsAsync(target.Id, ct);
        var forums = permittedForums.Where(f => !blockedForumIds.Contains(f.Id)).ToList();

        string? reason = null;
        var canRequest = false;
        if (viewerUserId == target.Id)
            reason = "You cannot request a conversation with yourself.";
        else if (conversation is not null)
            reason = "You already have a conversation.";
        else if (pending is not null)
            reason = pending.RequesterUserId == viewerUserId
                ? "A follow-back request is already pending."
                : "They already asked you to follow back.";
        else if (permittedForums.Count == 0)
            reason = "No shared forum grants you permission to request a follow-back for DMs.";
        else if (forums.Count == 0)
            reason = "They are not accepting follow-back requests from the forums you can use.";
        else
            canRequest = true;

        DirectConversationRequestDto? pendingDto = null;
        if (pending is not null)
            pendingDto = await ToRequestDtoAsync(pending, ct);

        return new DirectMessageEligibilityDto(
            ToPeer(target),
            canRequest,
            reason,
            conversation?.Id,
            pendingDto,
            forums);
    }

    public async Task<DirectConversationRequestDto> CreateRequestAsync(
        Guid requesterUserId,
        CreateDirectConversationRequestDto request,
        CancellationToken ct = default)
    {
        var target = await FindUserByUsernameAsync(request.TargetUsername, ct);
        if (requesterUserId == target.Id)
            throw new InvalidOperationException("You cannot request a conversation with yourself.");

        if (await FindConversationAsync(requesterUserId, target.Id, ct) is not null)
            throw new InvalidOperationException("You already have a conversation with this person.");

        if (await FindPendingBetweenAsync(requesterUserId, target.Id, ct) is not null)
            throw new InvalidOperationException("A follow-back request is already pending.");

        var forum = await ResolveForumForRequestAsync(
            requesterUserId,
            target.Id,
            request.ForumId,
            request.ForumSlug,
            request.ParentSlug,
            ct);

        await EnsureTargetAcceptsForumAsync(target.Id, forum.Id, ct);

        var intro = NormalizeMessage(request.Message, MaxRequestMessageLength);
        var entity = new DirectConversationRequest
        {
            RequesterUserId = requesterUserId,
            TargetUserId = target.Id,
            ForumId = forum.Id,
            Message = intro,
            Status = DirectConversationRequestStatuses.Pending
        };
        _db.DirectConversationRequests.Add(entity);
        await _db.SaveChangesAsync(ct);
        return await ToRequestDtoAsync(entity, ct);
    }

    public async Task<DirectConversationRequestDto> AcceptAsync(
        Guid targetUserId,
        Guid requestId,
        CancellationToken ct = default)
    {
        var entity = await _db.DirectConversationRequests
            .FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Request not found.");
        if (entity.TargetUserId != targetUserId)
            throw new PermissionDeniedException(nameof(ForumPermissions.RequestDirectMessage), "forum");
        if (entity.Status != DirectConversationRequestStatuses.Pending)
            throw new InvalidOperationException("This request is no longer pending.");

        var conversation = await GetOrCreateConversationAsync(
            entity.RequesterUserId,
            entity.TargetUserId,
            entity.ForumId,
            ct);

        if (!string.IsNullOrWhiteSpace(entity.Message))
        {
            _db.DirectMessages.Add(new DirectMessage
            {
                ConversationId = conversation.Id,
                AuthorUserId = entity.RequesterUserId,
                Body = entity.Message
            });
            conversation.UpdatedAt = DateTime.UtcNow;
        }

        entity.Status = DirectConversationRequestStatuses.Accepted;
        entity.ResolvedAt = DateTime.UtcNow;
        entity.ConversationId = conversation.Id;
        await _db.SaveChangesAsync(ct);
        return await ToRequestDtoAsync(entity, ct);
    }

    public async Task<DirectConversationRequestDto> DeclineAsync(
        Guid targetUserId,
        Guid requestId,
        CancellationToken ct = default)
    {
        var entity = await _db.DirectConversationRequests
            .FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Request not found.");
        if (entity.TargetUserId != targetUserId)
            throw new PermissionDeniedException(nameof(ForumPermissions.RequestDirectMessage), "forum");
        if (entity.Status != DirectConversationRequestStatuses.Pending)
            throw new InvalidOperationException("This request is no longer pending.");

        entity.Status = DirectConversationRequestStatuses.Declined;
        entity.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await ToRequestDtoAsync(entity, ct);
    }

    public async Task CancelAsync(Guid requesterUserId, Guid requestId, CancellationToken ct = default)
    {
        var entity = await _db.DirectConversationRequests
            .FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Request not found.");
        if (entity.RequesterUserId != requesterUserId)
            throw new PermissionDeniedException(nameof(ForumPermissions.RequestDirectMessage), "forum");
        if (entity.Status != DirectConversationRequestStatuses.Pending)
            throw new InvalidOperationException("This request is no longer pending.");

        entity.Status = DirectConversationRequestStatuses.Cancelled;
        entity.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<DirectMessageInboxDto> GetInboxAsync(Guid userId, CancellationToken ct = default)
    {
        var incoming = await _db.DirectConversationRequests.AsNoTracking()
            .Where(r => r.TargetUserId == userId && r.Status == DirectConversationRequestStatuses.Pending)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        var outgoing = await _db.DirectConversationRequests.AsNoTracking()
            .Where(r => r.RequesterUserId == userId && r.Status == DirectConversationRequestStatuses.Pending)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        var conversations = await _db.DirectConversations.AsNoTracking()
            .Where(c => c.UserLowId == userId || c.UserHighId == userId)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync(ct);

        var incomingDtos = new List<DirectConversationRequestDto>();
        foreach (var item in incoming)
            incomingDtos.Add(await ToRequestDtoAsync(item, ct));

        var outgoingDtos = new List<DirectConversationRequestDto>();
        foreach (var item in outgoing)
            outgoingDtos.Add(await ToRequestDtoAsync(item, ct));

        var conversationDtos = new List<DirectConversationSummaryDto>();
        foreach (var item in conversations)
            conversationDtos.Add(await ToSummaryDtoAsync(item, userId, ct));

        return new DirectMessageInboxDto(incomingDtos, outgoingDtos, conversationDtos);
    }

    public async Task<DirectConversationDto> GetConversationAsync(
        Guid userId,
        Guid conversationId,
        CancellationToken ct = default)
    {
        var conversation = await RequireParticipantAsync(userId, conversationId, ct);
        var messages = await _db.DirectMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        var peer = await GetPeerAsync(conversation, userId, ct);
        var forum = await TryForumOptionAsync(conversation.OriginForumId, ct);
        var users = await LoadUsersAsync(
            messages.Select(m => m.AuthorUserId).Append(peer.Id).Distinct(),
            ct);

        var messageDtos = messages.Select(m => new DirectMessageDto(
            m.Id,
            m.ConversationId,
            ToPeer(users[m.AuthorUserId]),
            m.Body,
            m.CreatedAt)).ToList();

        return new DirectConversationDto(conversation.Id, peer, forum, conversation.CreatedAt, messageDtos);
    }

    public async Task<DirectMessageDto> SendMessageAsync(
        Guid userId,
        Guid conversationId,
        string body,
        CancellationToken ct = default)
    {
        var conversation = await RequireParticipantAsync(userId, conversationId, ct);
        var text = NormalizeMessage(body, MaxDirectMessageLength)
            ?? throw new InvalidOperationException("Message cannot be empty.");

        var message = new DirectMessage
        {
            ConversationId = conversation.Id,
            AuthorUserId = userId,
            Body = text
        };
        _db.DirectMessages.Add(message);
        conversation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var author = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
        return new DirectMessageDto(message.Id, conversation.Id, ToPeer(author), message.Body, message.CreatedAt);
    }

    public async Task<DirectMessageForumPreferencesDto> ListForumPreferencesAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var membershipIds = await _db.ForumMemberships.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.ForumId)
            .ToListAsync(ct);
        var assignmentIds = await _db.ForumRoleAssignments.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => a.ForumId)
            .ToListAsync(ct);
        var requestForumIds = await _db.DirectConversationRequests.AsNoTracking()
            .Where(r => r.TargetUserId == userId)
            .Select(r => r.ForumId)
            .Distinct()
            .ToListAsync(ct);
        var prefs = await _db.DirectMessageForumPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync(ct);

        var forumIds = membershipIds
            .Concat(assignmentIds)
            .Concat(requestForumIds)
            .Concat(prefs.Select(p => p.ForumId))
            .ToHashSet();
        var visibleForums = await _db.Forums.AsNoTracking().Select(f => f.Id).ToListAsync(ct);
        foreach (var forumId in visibleForums)
        {
            if (forumIds.Contains(forumId)) continue;
            if (await _permissions.CanSeeForumAsync(userId, forumId, ct))
                forumIds.Add(forumId);
        }

        var modeByForum = prefs.ToDictionary(p => p.ForumId, p => p.Mode);
        var rows = new List<DirectMessageForumPreferenceDto>();
        foreach (var forumId in forumIds)
        {
            var option = await TryForumOptionAsync(forumId, ct);
            if (option is null) continue;
            if (!await _permissions.CanSeeForumAsync(userId, forumId, ct) &&
                !modeByForum.ContainsKey(forumId) &&
                !requestForumIds.Contains(forumId))
            {
                continue;
            }

            var mode = modeByForum.TryGetValue(forumId, out var stored)
                ? stored
                : DirectMessageForumPreferenceModes.None;
            rows.Add(new DirectMessageForumPreferenceDto(option, mode));
        }

        return new DirectMessageForumPreferencesDto(
            rows.OrderBy(r => r.Forum.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public async Task<DirectMessageForumPreferenceDto> SetForumPreferenceAsync(
        Guid userId,
        Guid forumId,
        string mode,
        CancellationToken ct = default)
    {
        var normalized = DirectMessageForumPreferenceModes.Normalize(mode);
        var forum = await _db.Forums.AsNoTracking().FirstOrDefaultAsync(f => f.Id == forumId, ct)
            ?? throw new InvalidOperationException("Forum not found.");

        var hasIncoming = await _db.DirectConversationRequests.AsNoTracking()
            .AnyAsync(r => r.TargetUserId == userId && r.ForumId == forumId, ct);
        if (!hasIncoming && !await _permissions.CanSeeForumAsync(userId, forum.Id, ct))
            throw new PermissionDeniedException(nameof(ForumPermissions.SeeForum), "forum");

        var existing = await _db.DirectMessageForumPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.ForumId == forumId, ct);

        if (!DirectMessageForumPreferenceModes.IsStored(normalized))
        {
            if (existing is not null)
                _db.DirectMessageForumPreferences.Remove(existing);
        }
        else if (existing is null)
        {
            _db.DirectMessageForumPreferences.Add(new DirectMessageForumPreference
            {
                UserId = userId,
                ForumId = forumId,
                Mode = normalized,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.Mode = normalized;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await ApplyPreferenceToPendingAsync(userId, forumId, normalized, ct);
        await _db.SaveChangesAsync(ct);

        var option = await TryForumOptionAsync(forumId, ct)
            ?? new DirectMessageForumOptionDto(forum.Id, forum.Name, forum.Slug, null);
        return new DirectMessageForumPreferenceDto(option, normalized);
    }

    private async Task ApplyPreferenceToPendingAsync(
        Guid userId,
        Guid forumId,
        string mode,
        CancellationToken ct)
    {
        if (!DirectMessageForumPreferenceModes.IsStored(mode)) return;

        var pending = await _db.DirectConversationRequests
            .Where(r =>
                r.TargetUserId == userId &&
                r.ForumId == forumId &&
                r.Status == DirectConversationRequestStatuses.Pending)
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var request in pending)
        {
            request.Status = mode == DirectMessageForumPreferenceModes.Reject
                ? DirectConversationRequestStatuses.Declined
                : DirectConversationRequestStatuses.Ignored;
            request.ResolvedAt = now;
        }
    }

    private async Task EnsureTargetAcceptsForumAsync(Guid targetUserId, Guid forumId, CancellationToken ct)
    {
        var mode = await GetPreferenceModeAsync(targetUserId, forumId, ct);
        if (mode == DirectMessageForumPreferenceModes.Reject)
            throw new InvalidOperationException("This person is not accepting follow-back requests from this forum.");
        if (mode == DirectMessageForumPreferenceModes.Ignore)
            throw new InvalidOperationException("This person is ignoring follow-back requests from this forum.");
    }

    private async Task<string> GetPreferenceModeAsync(Guid userId, Guid forumId, CancellationToken ct)
    {
        var mode = await _db.DirectMessageForumPreferences.AsNoTracking()
            .Where(p => p.UserId == userId && p.ForumId == forumId)
            .Select(p => p.Mode)
            .FirstOrDefaultAsync(ct);
        return DirectMessageForumPreferenceModes.IsStored(mode)
            ? mode!
            : DirectMessageForumPreferenceModes.None;
    }

    private async Task<HashSet<Guid>> LoadBlockedForumIdsAsync(Guid userId, CancellationToken ct)
    {
        var ids = await _db.DirectMessageForumPreferences.AsNoTracking()
            .Where(p => p.UserId == userId &&
                        (p.Mode == DirectMessageForumPreferenceModes.Ignore ||
                         p.Mode == DirectMessageForumPreferenceModes.Reject))
            .Select(p => p.ForumId)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    private async Task<DirectConversation> RequireParticipantAsync(
        Guid userId,
        Guid conversationId,
        CancellationToken ct)
    {
        var conversation = await _db.DirectConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new InvalidOperationException("Conversation not found.");
        if (conversation.UserLowId != userId && conversation.UserHighId != userId)
            throw new PermissionDeniedException(nameof(ForumPermissions.RequestDirectMessage), "forum");
        return conversation;
    }

    private async Task<DirectConversation> GetOrCreateConversationAsync(
        Guid userA,
        Guid userB,
        Guid originForumId,
        CancellationToken ct)
    {
        var existing = await FindConversationAsync(userA, userB, ct);
        if (existing is not null)
            return existing;

        OrderPair(userA, userB, out var low, out var high);
        var conversation = new DirectConversation
        {
            UserLowId = low,
            UserHighId = high,
            OriginForumId = originForumId
        };
        _db.DirectConversations.Add(conversation);
        await _db.SaveChangesAsync(ct);
        return conversation;
    }

    private async Task<Forum> ResolveForumForRequestAsync(
        Guid requesterUserId,
        Guid targetUserId,
        Guid? forumId,
        string? forumSlug,
        string? parentSlug,
        CancellationToken ct)
    {
        Forum? forum = null;
        if (forumId.HasValue)
        {
            forum = await _db.Forums.AsNoTracking().FirstOrDefaultAsync(f => f.Id == forumId.Value, ct);
        }
        else if (!string.IsNullOrWhiteSpace(forumSlug))
        {
            forum = await FindForumBySlugAsync(forumSlug, parentSlug, ct);
        }

        if (forumId.HasValue || !string.IsNullOrWhiteSpace(forumSlug))
        {
            if (forum is null)
                throw new InvalidOperationException("Forum not found.");
        }

        if (forum is null)
            throw new InvalidOperationException("Choose the forum whose permission you are using for this request.");

        if (!await ForumAllowsRequestAsync(forum.Id, requesterUserId, targetUserId, ct))
            throw new PermissionDeniedException(nameof(ForumPermissions.RequestDirectMessage), "forum");

        return forum;
    }

    private async Task<IReadOnlyList<DirectMessageForumOptionDto>> ListEligibleForumsAsync(
        Guid requesterUserId,
        Guid targetUserId,
        CancellationToken ct)
    {
        var forums = await _db.Forums.AsNoTracking()
            .Select(f => new { f.Id, f.Name, f.Slug, f.ParentForumId })
            .ToListAsync(ct);
        var parentSlugs = await LoadParentSlugsAsync(forums.Select(f => f.ParentForumId), ct);

        var result = new List<DirectMessageForumOptionDto>();
        foreach (var forum in forums)
        {
            if (!await ForumAllowsRequestAsync(forum.Id, requesterUserId, targetUserId, ct))
                continue;
            result.Add(new DirectMessageForumOptionDto(
                forum.Id,
                forum.Name,
                forum.Slug,
                forum.ParentForumId is Guid parentId ? parentSlugs.GetValueOrDefault(parentId) : null));
        }

        return result;
    }

    private async Task<bool> ForumAllowsRequestAsync(
        Guid forumId,
        Guid requesterUserId,
        Guid targetUserId,
        CancellationToken ct)
    {
        if (await _moderation.IsBannedAsync(forumId, requesterUserId, ct) ||
            await _moderation.IsBannedAsync(forumId, targetUserId, ct))
        {
            return false;
        }

        if (!await _permissions.CanViewForumAsync(targetUserId, forumId, ct))
            return false;

        var modes = await _permissions.GetForumPermissionModesAsync(requesterUserId, forumId, ct);
        return PermissionModeMap.AtLeast(modes, ForumPermissions.RequestDirectMessage, GrantMode.Direct);
    }

    private async Task<Forum?> FindForumBySlugAsync(string slug, string? parentSlug, CancellationToken ct)
    {
        var normalized = slug.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(parentSlug))
        {
            return await _db.Forums.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Slug == normalized && f.ParentForumId == null, ct);
        }

        var parent = await _db.Forums.AsNoTracking().FirstOrDefaultAsync(
            f => f.Slug == parentSlug.ToLowerInvariant() && f.ParentForumId == null, ct);
        if (parent is null) return null;
        return await _db.Forums.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Slug == normalized && f.ParentForumId == parent.Id, ct);
    }

    private async Task<DirectConversationRequest?> FindPendingBetweenAsync(Guid userA, Guid userB, CancellationToken ct) =>
        await _db.DirectConversationRequests.AsNoTracking().FirstOrDefaultAsync(
            r => r.Status == DirectConversationRequestStatuses.Pending &&
                 ((r.RequesterUserId == userA && r.TargetUserId == userB) ||
                  (r.RequesterUserId == userB && r.TargetUserId == userA)),
            ct);

    private async Task<DirectConversation?> FindConversationAsync(Guid userA, Guid userB, CancellationToken ct)
    {
        OrderPair(userA, userB, out var low, out var high);
        return await _db.DirectConversations.FirstOrDefaultAsync(
            c => c.UserLowId == low && c.UserHighId == high, ct);
    }

    private async Task<User> FindUserByUsernameAsync(string username, CancellationToken ct)
    {
        var normalized = (username ?? "").Trim();
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == normalized, ct);
        return user ?? throw new InvalidOperationException("User not found.");
    }

    private async Task<DirectConversationRequestDto> ToRequestDtoAsync(
        DirectConversationRequest entity,
        CancellationToken ct)
    {
        var users = await LoadUsersAsync([entity.RequesterUserId, entity.TargetUserId], ct);
        var forum = await _db.Forums.AsNoTracking().FirstAsync(f => f.Id == entity.ForumId, ct);
        string? parentSlug = null;
        if (forum.ParentForumId is Guid parentId)
        {
            parentSlug = await _db.Forums.AsNoTracking()
                .Where(f => f.Id == parentId)
                .Select(f => f.Slug)
                .FirstOrDefaultAsync(ct);
        }

        return new DirectConversationRequestDto(
            entity.Id,
            ToPeer(users[entity.RequesterUserId]),
            ToPeer(users[entity.TargetUserId]),
            new DirectMessageForumOptionDto(forum.Id, forum.Name, forum.Slug, parentSlug),
            entity.Message,
            entity.Status,
            entity.CreatedAt,
            entity.ResolvedAt,
            entity.ConversationId);
    }

    private async Task<DirectConversationSummaryDto> ToSummaryDtoAsync(
        DirectConversation conversation,
        Guid viewerUserId,
        CancellationToken ct)
    {
        var peer = await GetPeerAsync(conversation, viewerUserId, ct);
        var forum = await TryForumOptionAsync(conversation.OriginForumId, ct);
        var last = await _db.DirectMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.Body)
            .FirstOrDefaultAsync(ct);
        var preview = string.IsNullOrWhiteSpace(last)
            ? null
            : last.Length <= 140 ? last : last[..137] + "...";
        return new DirectConversationSummaryDto(conversation.Id, peer, forum, preview, conversation.UpdatedAt);
    }

    private async Task<DirectMessagePeerDto> GetPeerAsync(
        DirectConversation conversation,
        Guid viewerUserId,
        CancellationToken ct)
    {
        var peerId = conversation.UserLowId == viewerUserId ? conversation.UserHighId : conversation.UserLowId;
        var peer = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == peerId, ct);
        return ToPeer(peer);
    }

    private async Task<DirectMessageForumOptionDto?> TryForumOptionAsync(Guid? forumId, CancellationToken ct)
    {
        if (!forumId.HasValue) return null;
        var forum = await _db.Forums.AsNoTracking().FirstOrDefaultAsync(f => f.Id == forumId.Value, ct);
        if (forum is null) return null;
        string? parentSlug = null;
        if (forum.ParentForumId is Guid parentId)
        {
            parentSlug = await _db.Forums.AsNoTracking()
                .Where(f => f.Id == parentId)
                .Select(f => f.Slug)
                .FirstOrDefaultAsync(ct);
        }

        return new DirectMessageForumOptionDto(forum.Id, forum.Name, forum.Slug, parentSlug);
    }

    private async Task<Dictionary<Guid, User>> LoadUsersAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var idList = ids.Distinct().ToList();
        var users = await _db.Users.AsNoTracking().Where(u => idList.Contains(u.Id)).ToListAsync(ct);
        return users.ToDictionary(u => u.Id);
    }

    private async Task<Dictionary<Guid, string>> LoadParentSlugsAsync(
        IEnumerable<Guid?> parentIds,
        CancellationToken ct)
    {
        var ids = parentIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0) return [];
        return await _db.Forums.AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Slug, ct);
    }

    private static DirectMessagePeerDto ToPeer(User user) =>
        new(user.Id, user.Username, user.DisplayName, user.Did);

    private static void OrderPair(Guid a, Guid b, out Guid low, out Guid high)
    {
        if (a.CompareTo(b) <= 0)
        {
            low = a;
            high = b;
        }
        else
        {
            low = b;
            high = a;
        }
    }

    private static string? NormalizeMessage(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
