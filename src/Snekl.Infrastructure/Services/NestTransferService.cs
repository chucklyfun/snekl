using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface INestTransferService
{
    Task<IReadOnlyList<NestMapNodeDto>> ListAsync(Guid serverId, CancellationToken ct = default);
    Task MoveAsync(Guid sourceServerId, Guid forumId, Guid destinationServerId, CancellationToken ct = default);
}

public sealed class NestTransferService : INestTransferService
{
    private readonly SneklDbContext _db;

    public NestTransferService(SneklDbContext db) => _db = db;

    public async Task<IReadOnlyList<NestMapNodeDto>> ListAsync(Guid serverId, CancellationToken ct = default)
    {
        var forums = await _db.Forums.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.ServerId == serverId)
            .ToListAsync(ct);
        var parents = forums.ToDictionary(f => f.Id, f => f.ParentForumId);
        var byRef = forums.Select(f => new NestRef(f.Id, f.ParentForumId, f.Slug)).ToDictionary(n => n.Id);
        return forums
            .OrderBy(f => Depth(f.Id, parents))
            .ThenBy(f => f.SortOrder)
            .ThenBy(f => f.Name)
            .Select(f =>
            {
                var address = NestTopology.PublicAddress(f.Id, byRef);
                return new NestMapNodeDto(
                    f.Id,
                    f.Name,
                    f.Slug,
                    f.ParentForumId,
                    address.TopSlug,
                    f.SortOrder,
                    f.Locked,
                    f.Hidden,
                    Depth(f.Id, parents));
            })
            .ToList();
    }

    public async Task MoveAsync(Guid sourceServerId, Guid forumId, Guid destinationServerId, CancellationToken ct = default)
    {
        if (sourceServerId == destinationServerId)
            throw new InvalidOperationException("Choose a different server.");

        var destination = await _db.NodeConfig.AsNoTracking().FirstOrDefaultAsync(n => n.Id == destinationServerId, ct)
            ?? throw new InvalidOperationException("Destination server not found.");
        if (destination.DisabledAt is not null)
            throw new InvalidOperationException("Destination server is disabled.");
        if (!await _db.NodeConfig.AnyAsync(n => n.Id == sourceServerId, ct))
            throw new InvalidOperationException("Source server not found.");

        var sourceForums = await _db.Forums.IgnoreQueryFilters()
            .Where(f => f.ServerId == sourceServerId)
            .ToListAsync(ct);
        var byEntity = sourceForums.ToDictionary(f => f.Id);
        if (!byEntity.ContainsKey(forumId))
            throw new InvalidOperationException("Nest not found.");

        var parents = sourceForums.ToDictionary(f => f.Id, f => f.ParentForumId);
        var branchIds = NestTopology.BranchIds(forumId, parents);
        var byRef = sourceForums.Select(f => new NestRef(f.Id, f.ParentForumId, f.Slug)).ToDictionary(n => n.Id);
        var oldAddresses = branchIds
            .Select(id => (Id: id, Address: NestTopology.PublicAddress(id, byRef)))
            .ToList();

        var branch = branchIds.Select(id => byEntity[id]).ToList();
        var root = byEntity[forumId];
        root.ParentForumId = null;

        var destinationForums = await _db.Forums.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.ServerId == destinationServerId)
            .Select(f => new { f.ParentForumId, f.Slug })
            .ToListAsync(ct);
        var rootSlugs = destinationForums
            .Where(f => f.ParentForumId is null)
            .Select(f => f.Slug)
            .ToHashSet(StringComparer.Ordinal);
        root.Slug = NestTopology.NextSlug(root.Slug, rootSlugs);

        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nest in branch.Where(f => f.Id != root.Id))
        {
            nest.Slug = NestTopology.NextSlug(nest.Slug, taken);
            taken.Add(nest.Slug);
        }

        var now = DateTime.UtcNow;
        foreach (var nest in branch)
        {
            nest.Locked = true;
            nest.ServerId = destinationServerId;
            nest.UpdatedAt = now;
        }

        var relational = _db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) != true;
        if (relational)
            await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var userMap = await RemapGraphAsync(branchIds, sourceServerId, destinationServerId, ct);
            foreach (var nest in branch)
            {
                nest.CreatedByUserId = userMap.Map(nest.CreatedByUserId);
                if (nest.OwnerUserId is Guid owner)
                    nest.OwnerUserId = userMap.Map(owner);
            }

            foreach (var (id, address) in oldAddresses)
                await NestRedirects.RememberAsync(_db, sourceServerId, address.TopSlug, address.Slug, id, ct);

            await _db.SaveChangesAsync(ct);
            if (relational)
                await _db.Database.CommitTransactionAsync(ct);
        }
        catch
        {
            if (relational)
                await _db.Database.RollbackTransactionAsync(ct);
            throw;
        }
    }

    private async Task<UserMap> RemapGraphAsync(
        IReadOnlyList<Guid> forumIds,
        Guid sourceServerId,
        Guid destinationServerId,
        CancellationToken ct)
    {
        var forumIdSet = forumIds.ToHashSet();
        var coolers = await _db.Watercoolers.IgnoreQueryFilters()
            .Where(w => w.ParentForumId != null && forumIdSet.Contains(w.ParentForumId.Value))
            .ToListAsync(ct);
        var coolerIds = coolers.Select(w => w.Id).ToHashSet();
        var posts = await _db.Posts.IgnoreQueryFilters()
            .Where(p =>
                (p.ForumId != null && forumIdSet.Contains(p.ForumId.Value)) ||
                (p.WatercoolerId != null && coolerIds.Contains(p.WatercoolerId.Value)))
            .ToListAsync(ct);
        var postIds = posts.Select(p => p.Id).ToHashSet();

        var roles = await _db.ForumRoles.IgnoreQueryFilters().Where(r => forumIdSet.Contains(r.ForumId)).ToListAsync(ct);
        var roleAssignments = await _db.ForumRoleAssignments.IgnoreQueryFilters().Where(a => forumIdSet.Contains(a.ForumId)).ToListAsync(ct);
        var memberships = await _db.ForumMemberships.IgnoreQueryFilters().Where(m => forumIdSet.Contains(m.ForumId)).ToListAsync(ct);
        var bans = await _db.ForumMemberBans.IgnoreQueryFilters().Where(b => forumIdSet.Contains(b.ForumId)).ToListAsync(ct);
        var flairTemplates = await _db.ForumFlairTemplates.IgnoreQueryFilters().Where(t => forumIdSet.Contains(t.ForumId)).ToListAsync(ct);
        var memberFlairs = await _db.ForumMemberFlairs.IgnoreQueryFilters().Where(f => forumIdSet.Contains(f.ForumId)).ToListAsync(ct);
        var aliases = await _db.ForumMemberAliases.IgnoreQueryFilters().Where(a => forumIdSet.Contains(a.ForumId)).ToListAsync(ct);
        var pages = await _db.ForumPages.IgnoreQueryFilters().Where(p => forumIdSet.Contains(p.ForumId)).ToListAsync(ct);
        var blocks = await _db.ForumContentBlocks.IgnoreQueryFilters().Where(b => forumIdSet.Contains(b.ForumId)).ToListAsync(ct);
        var coolerRoles = await _db.WatercoolerRoles.IgnoreQueryFilters().Where(r => coolerIds.Contains(r.WatercoolerId)).ToListAsync(ct);
        var coolerAssignments = await _db.WatercoolerRoleAssignments.IgnoreQueryFilters().Where(a => coolerIds.Contains(a.WatercoolerId)).ToListAsync(ct);
        var reactions = await _db.PostReactions.IgnoreQueryFilters().Where(r => postIds.Contains(r.PostId)).ToListAsync(ct);
        var revisions = await _db.PostRevisions.IgnoreQueryFilters().Where(r => postIds.Contains(r.PostId)).ToListAsync(ct);
        var reports = await _db.PostReports.IgnoreQueryFilters().Where(r => forumIdSet.Contains(r.ForumId)).ToListAsync(ct);
        var upvotes = await _db.PostUpvotes.IgnoreQueryFilters().Where(u => postIds.Contains(u.PostId)).ToListAsync(ct);
        var postMedia = await _db.PostMedia.IgnoreQueryFilters().Where(m => postIds.Contains(m.PostId)).ToListAsync(ct);
        var emotes = await _db.Emotes.IgnoreQueryFilters().Where(e => e.ForumId != null && forumIdSet.Contains(e.ForumId.Value)).ToListAsync(ct);
        var installs = await _db.ExtensionInstalls.IgnoreQueryFilters().Where(i => i.ForumId != null && forumIdSet.Contains(i.ForumId.Value)).ToListAsync(ct);
        var changes = await _db.ChangeLogEntries.IgnoreQueryFilters()
            .Where(c =>
                (c.ForumId != null && forumIdSet.Contains(c.ForumId.Value)) ||
                (c.WatercoolerId != null && coolerIds.Contains(c.WatercoolerId.Value)))
            .ToListAsync(ct);
        var requests = await _db.DirectConversationRequests.IgnoreQueryFilters().Where(r => forumIdSet.Contains(r.ForumId)).ToListAsync(ct);
        var conversations = await _db.DirectConversations.IgnoreQueryFilters()
            .Where(c => c.OriginForumId != null && forumIdSet.Contains(c.OriginForumId.Value))
            .ToListAsync(ct);
        var conversationIds = conversations.Select(c => c.Id).ToHashSet();
        var messages = await _db.DirectMessages.IgnoreQueryFilters().Where(m => conversationIds.Contains(m.ConversationId)).ToListAsync(ct);
        var preferences = await _db.DirectMessageForumPreferences.IgnoreQueryFilters().Where(p => forumIdSet.Contains(p.ForumId)).ToListAsync(ct);
        var threadAlerts = await _db.ThreadAlertPreferences.IgnoreQueryFilters().Where(a => postIds.Contains(a.RootPostId)).ToListAsync(ct);
        var replyAlerts = await _db.ReplyAlerts.IgnoreQueryFilters()
            .Where(a =>
                (a.RootPostId != null && postIds.Contains(a.RootPostId.Value)) ||
                (a.ForumId != null && forumIdSet.Contains(a.ForumId.Value)) ||
                (a.WatercoolerId != null && coolerIds.Contains(a.WatercoolerId.Value)))
            .ToListAsync(ct);
        var placeAlerts = await _db.PlaceAlertPreferences.IgnoreQueryFilters()
            .Where(p =>
                (p.ForumId != null && forumIdSet.Contains(p.ForumId.Value)) ||
                (p.WatercoolerId != null && coolerIds.Contains(p.WatercoolerId.Value)))
            .ToListAsync(ct);

        var forums = await _db.Forums.IgnoreQueryFilters().Where(f => forumIdSet.Contains(f.Id)).ToListAsync(ct);
        var userIds = new HashSet<Guid>();
        void Take(Guid id) { if (id != Guid.Empty) userIds.Add(id); }
        void TakeOpt(Guid? id) { if (id is Guid value) Take(value); }
        foreach (var forum in forums)
        {
            Take(forum.CreatedByUserId);
            TakeOpt(forum.OwnerUserId);
        }
        foreach (var row in roleAssignments) Take(row.UserId);
        foreach (var row in memberships) Take(row.UserId);
        foreach (var row in bans) { Take(row.UserId); Take(row.BannedByUserId); }
        foreach (var row in memberFlairs) Take(row.UserId);
        foreach (var row in aliases) Take(row.UserId);
        foreach (var row in coolers) Take(row.CreatedByUserId);
        foreach (var row in coolerAssignments) Take(row.UserId);
        foreach (var row in posts) Take(row.AuthorUserId);
        foreach (var row in reactions) Take(row.UserId);
        foreach (var row in revisions) Take(row.EditedByUserId);
        foreach (var row in reports) { Take(row.ReporterUserId); TakeOpt(row.ResolvedByUserId); }
        foreach (var row in upvotes) Take(row.UserId);
        foreach (var row in emotes) Take(row.CreatedByUserId);
        foreach (var row in installs) Take(row.InstalledByUserId);
        foreach (var row in changes) TakeOpt(row.ActorUserId);
        foreach (var row in requests) { Take(row.RequesterUserId); Take(row.TargetUserId); }
        foreach (var row in conversations) { Take(row.UserLowId); Take(row.UserHighId); }
        foreach (var row in messages) Take(row.AuthorUserId);
        foreach (var row in preferences) Take(row.UserId);
        foreach (var row in threadAlerts) Take(row.UserId);
        foreach (var row in replyAlerts)
        {
            Take(row.RecipientUserId);
            TakeOpt(row.SubjectUserId);
        }
        foreach (var row in placeAlerts) Take(row.UserId);

        var mediaIds = forums.SelectMany(f => new[] { f.AvatarMediaAssetId, f.BannerMediaAssetId })
            .Concat(postMedia.Select(m => (Guid?)m.MediaAssetId))
            .Concat(emotes.Select(e => e.MediaAssetId))
            .Where(id => id is Guid)
            .Select(id => id!.Value)
            .ToHashSet();
        var media = await _db.MediaAssets.IgnoreQueryFilters().Where(m => mediaIds.Contains(m.Id)).ToListAsync(ct);
        foreach (var asset in media) Take(asset.UploaderUserId);

        var map = await UserMap.CreateAsync(_db, userIds, sourceServerId, destinationServerId, ct);
        await _db.SaveChangesAsync(ct);

        void Server(IServerScoped row) => row.ServerId = destinationServerId;
        foreach (var row in roles) Server(row);
        foreach (var row in roleAssignments) { Server(row); row.UserId = map.Map(row.UserId); }
        foreach (var row in memberships) { Server(row); row.UserId = map.Map(row.UserId); }
        foreach (var row in bans)
        {
            Server(row);
            row.UserId = map.Map(row.UserId);
            row.BannedByUserId = map.Map(row.BannedByUserId);
        }
        foreach (var row in flairTemplates) Server(row);
        foreach (var row in memberFlairs) { Server(row); row.UserId = map.Map(row.UserId); }
        foreach (var row in aliases) { Server(row); row.UserId = map.Map(row.UserId); }
        foreach (var row in pages) Server(row);
        foreach (var row in blocks) Server(row);
        foreach (var row in coolers) { Server(row); row.CreatedByUserId = map.Map(row.CreatedByUserId); }
        foreach (var row in coolerRoles) Server(row);
        foreach (var row in coolerAssignments) { Server(row); row.UserId = map.Map(row.UserId); }
        foreach (var row in posts) { Server(row); row.AuthorUserId = map.Map(row.AuthorUserId); }
        foreach (var row in reactions) { Server(row); row.UserId = map.Map(row.UserId); }
        foreach (var row in revisions) { Server(row); row.EditedByUserId = map.Map(row.EditedByUserId); }
        foreach (var row in reports)
        {
            Server(row);
            row.ReporterUserId = map.Map(row.ReporterUserId);
            if (row.ResolvedByUserId is Guid resolver)
                row.ResolvedByUserId = map.Map(resolver);
        }
        foreach (var row in upvotes) { Server(row); row.UserId = map.Map(row.UserId); }
        foreach (var row in postMedia) Server(row);
        foreach (var row in emotes) { Server(row); row.CreatedByUserId = map.Map(row.CreatedByUserId); }
        foreach (var row in installs) { Server(row); row.InstalledByUserId = map.Map(row.InstalledByUserId); }
        foreach (var row in changes)
        {
            Server(row);
            if (row.ActorUserId is Guid actor)
                row.ActorUserId = map.Map(actor);
        }
        foreach (var row in requests)
        {
            Server(row);
            row.RequesterUserId = map.Map(row.RequesterUserId);
            row.TargetUserId = map.Map(row.TargetUserId);
        }
        foreach (var row in conversations)
        {
            Server(row);
            row.UserLowId = map.Map(row.UserLowId);
            row.UserHighId = map.Map(row.UserHighId);
            if (row.UserLowId > row.UserHighId)
                (row.UserLowId, row.UserHighId) = (row.UserHighId, row.UserLowId);
        }
        foreach (var row in messages) { Server(row); row.AuthorUserId = map.Map(row.AuthorUserId); }
        foreach (var row in preferences) { Server(row); row.UserId = map.Map(row.UserId); }
        foreach (var row in threadAlerts) { Server(row); row.UserId = map.Map(row.UserId); }
        foreach (var row in replyAlerts)
        {
            Server(row);
            row.RecipientUserId = map.Map(row.RecipientUserId);
            if (row.SubjectUserId is Guid subjectUserId)
                row.SubjectUserId = map.Map(subjectUserId);
        }
        foreach (var row in placeAlerts) { Server(row); row.UserId = map.Map(row.UserId); }
        foreach (var row in media) { Server(row); row.UploaderUserId = map.Map(row.UploaderUserId); }

        return map;
    }

    private static int Depth(Guid id, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        var depth = 0;
        while (parents.TryGetValue(id, out var parent) && parent is Guid next && depth < NestTopology.MaxDepth)
        {
            depth++;
            id = next;
        }

        return depth;
    }

    private sealed class UserMap
    {
        private readonly Dictionary<Guid, Guid> _map;

        private UserMap(Dictionary<Guid, Guid> map) => _map = map;

        public Guid Map(Guid sourceUserId) =>
            _map.TryGetValue(sourceUserId, out var dest) ? dest : sourceUserId;

        public static async Task<UserMap> CreateAsync(
            SneklDbContext db,
            IEnumerable<Guid> sourceUserIds,
            Guid sourceServerId,
            Guid destinationServerId,
            CancellationToken ct)
        {
            var map = new Dictionary<Guid, Guid>();
            var usernames = await db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.ServerId == destinationServerId)
                .Select(u => u.Username)
                .ToListAsync(ct);
            var emails = await db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.ServerId == destinationServerId)
                .Select(u => u.Email)
                .ToListAsync(ct);
            var usedNames = usernames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var usedEmails = emails.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var sourceUserId in sourceUserIds.Distinct())
            {
                var source = await db.Users.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.Id == sourceUserId && u.ServerId == sourceServerId, ct);
                if (source is null)
                {
                    map[sourceUserId] = sourceUserId;
                    continue;
                }

                User? dest = null;
                if (!string.IsNullOrWhiteSpace(source.Did))
                {
                    dest = await db.Users.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(u => u.ServerId == destinationServerId && u.Did == source.Did, ct);
                }

                if (dest is null && source.AuthSubjectId is Guid subject && !string.IsNullOrWhiteSpace(source.AuthIssuerUrl))
                {
                    dest = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(
                        u => u.ServerId == destinationServerId && u.AuthIssuerUrl == source.AuthIssuerUrl && u.AuthSubjectId == subject,
                        ct);
                }

                if (dest is null)
                {
                    var username = source.Username;
                    var suffix = 2;
                    while (!usedNames.Add(username))
                        username = $"{source.Username}-{suffix++}";

                    var email = string.IsNullOrWhiteSpace(source.Email)
                        ? $"moved-{source.Id:N}@users.invalid"
                        : source.Email;
                    if (!usedEmails.Add(email))
                        email = $"moved-{source.Id:N}@users.invalid";

                    dest = new User
                    {
                        ServerId = destinationServerId,
                        Username = username,
                        DisplayName = source.DisplayName,
                        Email = email,
                        PasswordHash = source.PasswordHash,
                        Bio = source.Bio,
                        Did = source.Did,
                        AuthIssuerUrl = source.AuthIssuerUrl,
                        AuthSubjectId = source.AuthSubjectId,
                        IsBot = source.IsBot
                    };
                    db.Users.Add(dest);
                }

                map[sourceUserId] = dest.Id;
            }

            return new UserMap(map);
        }
    }
}
