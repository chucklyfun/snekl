using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Snekl.Domain;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IForumRoleService
{
    Task<IReadOnlyList<ForumRoleDto>> ListRolesAsync(Guid forumId, CancellationToken ct = default);
    Task<ForumRoleDto> CreateRoleAsync(Guid forumId, CreateForumRoleRequest request, Guid? actorUserId = null, CancellationToken ct = default);
    Task<ForumRoleDto> UpdateRoleAsync(Guid forumId, Guid roleId, UpdateForumRoleRequest request, Guid? actorUserId = null, CancellationToken ct = default);
    Task DeleteRoleAsync(Guid forumId, Guid roleId, Guid? actorUserId = null, CancellationToken ct = default);
    Task<IReadOnlyList<ForumMemberDto>> ListMembersAsync(Guid forumId, CancellationToken ct = default);
    Task AssignRolesAsync(Guid forumId, Guid userId, IReadOnlyList<Guid> roleIds, Guid? actorUserId = null, CancellationToken ct = default);
    Task AssignRolesByUsernameAsync(Guid forumId, string username, IReadOnlyList<Guid> roleIds, Guid? actorUserId = null, CancellationToken ct = default);
}

public class ForumRoleService : IForumRoleService
{
    private readonly SneklDbContext _db;
    private readonly IHubFeedPublisher? _hubFeed;
    private readonly IChangeLogService? _changeLog;
    private readonly IPermissionService _permissions;

    public ForumRoleService(
        SneklDbContext db,
        IPermissionService permissions,
        IHubFeedPublisher? hubFeed = null,
        IChangeLogService? changeLog = null)
    {
        _db = db;
        _permissions = permissions;
        _hubFeed = hubFeed;
        _changeLog = changeLog;
    }

    public async Task<IReadOnlyList<ForumRoleDto>> ListRolesAsync(Guid forumId, CancellationToken ct = default)
    {
        var roles = await _db.ForumRoles
            .Where(r => r.ForumId == forumId)
            .OrderBy(r => r.Position)
            .ToListAsync(ct);
        return roles.Select(ToDto).ToList();
    }

    public async Task<ForumRoleDto> CreateRoleAsync(Guid forumId, CreateForumRoleRequest request, Guid? actorUserId = null, CancellationToken ct = default)
    {
        if (request.Name.Trim().Equals("@everyone", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot create a role named @everyone.");

        var role = new ForumRole
        {
            ForumId = forumId,
            Name = request.Name.Trim(),
            Color = request.Color,
            Position = request.Position,
            Modes = PermissionParsing.ParseForumModes(request.Permissions, await CustomForumNamesAsync(ct))
        };
        _db.ForumRoles.Add(role);
        await _db.SaveChangesAsync(ct);
        await RecordRoleAsync(forumId, role, ChangeLogActions.Created, actorUserId, $"Created role {role.Name}", null, RoleSnapshot(role), ct);
        return ToDto(role);
    }

    public async Task<ForumRoleDto> UpdateRoleAsync(Guid forumId, Guid roleId, UpdateForumRoleRequest request, Guid? actorUserId = null, CancellationToken ct = default)
    {
        var role = await _db.ForumRoles.FirstOrDefaultAsync(r => r.Id == roleId && r.ForumId == forumId, ct)
            ?? throw new InvalidOperationException("Role not found.");

        var before = RoleSnapshot(role);
        if (role.IsOwner)
        {
            if (!await _permissions.CanOverrideForumOwnerAsync(actorUserId, ct))
                throw new InvalidOperationException("Only server-level admins can change the Owner role.");
            if (!request.Permissions.Contains(nameof(ForumPermissions.ForumAdministrator), StringComparer.OrdinalIgnoreCase) &&
                !request.Permissions.Contains("ForumAdministrator", StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Owner role must keep ForumAdministrator.");
            role.IsOwner = true;
        }

        if (role.IsEveryone)
        {
            role.Color = request.Color;
            role.Modes = PermissionParsing.ParseForumModes(request.Permissions, await CustomForumNamesAsync(ct));
        }
        else
        {
            role.Name = request.Name.Trim();
            role.Color = request.Color;
            role.Position = request.Position;
            role.Modes = PermissionParsing.ParseForumModes(request.Permissions, await CustomForumNamesAsync(ct));
        }

        await _db.SaveChangesAsync(ct);
        await RecordRoleAsync(forumId, role, ChangeLogActions.Updated, actorUserId, $"Updated role {role.Name}", before, RoleSnapshot(role), ct);
        return ToDto(role);
    }

    public async Task DeleteRoleAsync(Guid forumId, Guid roleId, Guid? actorUserId = null, CancellationToken ct = default)
    {
        var role = await _db.ForumRoles.FirstOrDefaultAsync(r => r.Id == roleId && r.ForumId == forumId, ct)
            ?? throw new InvalidOperationException("Role not found.");
        if (role.IsEveryone)
            throw new InvalidOperationException("Cannot delete @everyone role.");
        if (role.IsOwner && !await _permissions.CanOverrideForumOwnerAsync(actorUserId, ct))
            throw new InvalidOperationException("Only server-level admins can delete the Owner role.");

        var before = RoleSnapshot(role);
        var name = role.Name;
        _db.ForumRoles.Remove(role);
        await _db.SaveChangesAsync(ct);
        await RecordRoleAsync(forumId, role, ChangeLogActions.Deleted, actorUserId, $"Deleted role {name}", before, null, ct);
    }

    public async Task<IReadOnlyList<ForumMemberDto>> ListMembersAsync(Guid forumId, CancellationToken ct = default)
    {
        var assignments = await _db.ForumRoleAssignments
            .Include(a => a.User)
            .Include(a => a.Role)
            .Where(a => a.ForumId == forumId)
            .ToListAsync(ct);

        var flairs = await _db.ForumMemberFlairs
            .Include(f => f.Template)
            .Where(f => f.ForumId == forumId)
            .ToDictionaryAsync(f => f.UserId, ct);

        var aliases = await _db.ForumMemberAliases
            .Where(a => a.ForumId == forumId)
            .ToDictionaryAsync(a => a.UserId, a => a.Alias, ct);

        var grouped = assignments.GroupBy(a => a.UserId);
        var result = new List<ForumMemberDto>();

        foreach (var group in grouped)
        {
            var user = group.First().User!;
            flairs.TryGetValue(user.Id, out var flair);
            aliases.TryGetValue(user.Id, out var alias);
            result.Add(new ForumMemberDto(
                user.Id,
                user.Username,
                user.DisplayName,
                group.Select(a => ToDto(a.Role!)).ToList(),
                flair?.Template is not null ? ToFlairDto(flair.Template) : null,
                alias));
        }

        return result.OrderBy(m => m.Username).ToList();
    }

    public async Task AssignRolesAsync(Guid forumId, Guid userId, IReadOnlyList<Guid> roleIds, Guid? actorUserId = null, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct)
            ?? throw new InvalidOperationException("User not found.");

        var forum = await _db.Forums.AsNoTracking().FirstOrDefaultAsync(f => f.Id == forumId, ct)
            ?? throw new InvalidOperationException("Forum not found.");

        var ownerRoleIds = await _db.ForumRoles
            .Where(r => r.ForumId == forumId && r.IsOwner)
            .Select(r => r.Id)
            .ToListAsync(ct);

        var keepsOwnerRole = ownerRoleIds.Count == 0 || roleIds.Any(id => ownerRoleIds.Contains(id));
        if (forum.OwnerUserId == userId && !keepsOwnerRole &&
            !await _permissions.CanOverrideForumOwnerAsync(actorUserId, ct))
        {
            throw new InvalidOperationException("Other forum admins cannot remove the Owner role from the forum owner.");
        }

        var validRoleIds = await _db.ForumRoles
            .Where(r => r.ForumId == forumId && roleIds.Contains(r.Id) && !r.IsEveryone)
            .Select(r => r.Id)
            .ToListAsync(ct);

        var existing = await _db.ForumRoleAssignments
            .Where(a => a.ForumId == forumId && a.UserId == userId)
            .ToListAsync(ct);
        var beforeIds = existing.Select(a => a.RoleId).OrderBy(id => id).ToList();
        var wasMember = existing.Count > 0;
        _db.ForumRoleAssignments.RemoveRange(existing);

        foreach (var roleId in validRoleIds)
        {
            _db.ForumRoleAssignments.Add(new ForumRoleAssignment
            {
                ForumId = forumId,
                UserId = userId,
                RoleId = roleId
            });
        }

        await _db.SaveChangesAsync(ct);

        if (forum.OwnerUserId == userId && !validRoleIds.Any(id => ownerRoleIds.Contains(id)))
        {
            var tracked = await _db.Forums.FirstOrDefaultAsync(f => f.Id == forumId, ct);
            if (tracked is not null)
            {
                tracked.OwnerUserId = null;
                await _db.SaveChangesAsync(ct);
            }
        }

        var isMember = validRoleIds.Count > 0;
        if (isMember)
        {
            if (!await _db.ForumMemberships.AnyAsync(m => m.ForumId == forumId && m.UserId == userId, ct))
            {
                _db.ForumMemberships.Add(new ForumMembership
                {
                    ForumId = forumId,
                    UserId = userId,
                    IsFavorite = false,
                    UpdatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(ct);
            }
        }
        else
        {
            _db.ForumMemberships.RemoveRange(
                await _db.ForumMemberships.Where(m => m.ForumId == forumId && m.UserId == userId).ToListAsync(ct));
            await _db.SaveChangesAsync(ct);
        }

        if (wasMember != isMember)
            await PublishMembershipAsync(forumId, userId, deleted: !isMember, ct);

        var afterIds = validRoleIds.OrderBy(id => id).ToList();
        if (_changeLog is not null && !beforeIds.SequenceEqual(afterIds))
        {
            await _changeLog.RecordAsync(
                ChangeLogScopes.Forum,
                forumId,
                null,
                ChangeLogEntityTypes.ForumMember,
                userId,
                ChangeLogActions.Assigned,
                actorUserId,
                $"Updated roles for @{user.Username}",
                new { roleIds = beforeIds },
                new { roleIds = afterIds },
                ct);
        }
    }

    public async Task AssignRolesByUsernameAsync(Guid forumId, string username, IReadOnlyList<Guid> roleIds, Guid? actorUserId = null, CancellationToken ct = default)
    {
        var normalized = username.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == normalized, ct)
            ?? throw new InvalidOperationException("User not found.");
        await AssignRolesAsync(forumId, user.Id, roleIds, actorUserId, ct);
    }

    private static ForumRoleDto ToDto(ForumRole role)
    {
        var names = PermissionNames.ToNames(role.Modes, server: false).ToList();
        if (PermissionModeMap.Get(role.Modes, ForumPermissions.ForumAdministrator) == GrantMode.Direct &&
            !names.Contains(nameof(ForumPermissions.ForumAdministrator), StringComparer.OrdinalIgnoreCase))
        {
            names.Add(nameof(ForumPermissions.ForumAdministrator));
        }

        return new(
            role.Id,
            role.ForumId,
            role.Name,
            role.Color,
            role.Position,
            names,
            role.IsEveryone,
            role.InheritFromRoleId,
            role.IsOwner);
    }

    private static object RoleSnapshot(ForumRole role) => new
    {
        name = role.Name,
        color = role.Color,
        position = role.Position,
        permissions = PermissionNames.ToNames(role.Modes, server: false)
    };

    private async Task RecordRoleAsync(
        Guid forumId,
        ForumRole role,
        string action,
        Guid? actorUserId,
        string summary,
        object? before,
        object? after,
        CancellationToken ct)
    {
        if (_changeLog is null) return;
        await _changeLog.RecordAsync(
            ChangeLogScopes.Forum,
            forumId,
            null,
            ChangeLogEntityTypes.ForumRole,
            role.Id,
            action,
            actorUserId,
            summary,
            before,
            after,
            ct);
    }

    private async Task<HashSet<string>> CustomForumNamesAsync(CancellationToken ct)
    {
        var names = await _db.ContentBlockPermissions.AsNoTracking()
            .Where(p => p.Scope == ContentBlockScopes.Forum)
            .Select(p => p.Name)
            .ToListAsync(ct);
        return names.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static ForumFlairTemplateDto ToFlairDto(ForumFlairTemplate t) => new(
        t.Id, t.ForumId, t.Emoji, t.IconUrl, t.Label, t.NameColor, t.NameStyle, t.Position, t.InheritFromTemplateId);

    private async Task PublishMembershipAsync(Guid forumId, Guid userId, bool deleted, CancellationToken ct)
    {
        if (_hubFeed is null)
            return;

        var did = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Did)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(did))
            return;

        var isFavorite = !deleted && await _db.ForumMemberships.AsNoTracking()
            .Where(m => m.ForumId == forumId && m.UserId == userId)
            .Select(m => m.IsFavorite)
            .FirstOrDefaultAsync(ct);
        await _hubFeed.PublishForumMembershipAsync(did, forumId, deleted, isFavorite, ct);
    }
}

public interface IForumMemberService
{
    Task<IReadOnlyList<ForumFlairTemplateDto>> ListFlairTemplatesAsync(Guid forumId, CancellationToken ct = default);
    Task<ForumFlairTemplateDto> CreateFlairTemplateAsync(Guid forumId, CreateForumFlairTemplateRequest request, CancellationToken ct = default);
    Task<ForumFlairTemplateDto> UpdateFlairTemplateAsync(Guid forumId, Guid templateId, UpdateForumFlairTemplateRequest request, CancellationToken ct = default);
    Task DeleteFlairTemplateAsync(Guid forumId, Guid templateId, CancellationToken ct = default);
    Task AssignFlairAsync(Guid forumId, Guid userId, Guid? templateId, CancellationToken ct = default);
    Task SetAliasAsync(Guid forumId, Guid userId, string alias, CancellationToken ct = default);
    Task RemoveAliasAsync(Guid forumId, Guid userId, CancellationToken ct = default);
    Task UpdateForumSettingsAsync(Guid forumId, UpdateForumSettingsRequest request, Guid? actorUserId = null, CancellationToken ct = default);
}

public class ForumMemberService : IForumMemberService
{
    private static readonly Regex ForumSlugPattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly SneklDbContext _db;
    private readonly IChangeLogService? _changeLog;

    public ForumMemberService(SneklDbContext db, IChangeLogService? changeLog = null)
    {
        _db = db;
        _changeLog = changeLog;
    }

    public async Task<IReadOnlyList<ForumFlairTemplateDto>> ListFlairTemplatesAsync(Guid forumId, CancellationToken ct = default)
    {
        var templates = await _db.ForumFlairTemplates
            .Where(t => t.ForumId == forumId)
            .OrderBy(t => t.Position)
            .ToListAsync(ct);
        return templates.Select(t => new ForumFlairTemplateDto(
            t.Id, t.ForumId, t.Emoji, t.IconUrl, t.Label, t.NameColor, t.NameStyle, t.Position, t.InheritFromTemplateId)).ToList();
    }

    public async Task<ForumFlairTemplateDto> CreateFlairTemplateAsync(Guid forumId, CreateForumFlairTemplateRequest request, CancellationToken ct = default)
    {
        var template = new ForumFlairTemplate
        {
            ForumId = forumId,
            Emoji = request.Emoji,
            IconUrl = request.IconUrl,
            Label = request.Label,
            NameColor = request.NameColor,
            NameStyle = request.NameStyle,
            Position = request.Position
        };
        _db.ForumFlairTemplates.Add(template);
        await _db.SaveChangesAsync(ct);
        return new ForumFlairTemplateDto(
            template.Id, template.ForumId, template.Emoji, template.IconUrl, template.Label,
            template.NameColor, template.NameStyle, template.Position, template.InheritFromTemplateId);
    }

    public async Task<ForumFlairTemplateDto> UpdateFlairTemplateAsync(
        Guid forumId,
        Guid templateId,
        UpdateForumFlairTemplateRequest request,
        CancellationToken ct = default)
    {
        var template = await _db.ForumFlairTemplates.FirstOrDefaultAsync(t => t.Id == templateId && t.ForumId == forumId, ct)
            ?? throw new InvalidOperationException("Flair template not found.");
        template.Emoji = request.Emoji;
        template.IconUrl = request.IconUrl;
        template.Label = request.Label;
        template.NameColor = request.NameColor;
        template.NameStyle = request.NameStyle;
        if (request.Position.HasValue)
            template.Position = request.Position.Value;
        await _db.SaveChangesAsync(ct);
        return new ForumFlairTemplateDto(
            template.Id, template.ForumId, template.Emoji, template.IconUrl, template.Label,
            template.NameColor, template.NameStyle, template.Position, template.InheritFromTemplateId);
    }

    public async Task DeleteFlairTemplateAsync(Guid forumId, Guid templateId, CancellationToken ct = default)
    {
        var template = await _db.ForumFlairTemplates.FirstOrDefaultAsync(t => t.Id == templateId && t.ForumId == forumId, ct)
            ?? throw new InvalidOperationException("Flair template not found.");
        var assigned = await _db.ForumMemberFlairs.Where(f => f.TemplateId == templateId).ToListAsync(ct);
        _db.ForumMemberFlairs.RemoveRange(assigned);
        var inherited = await _db.ForumFlairTemplates.Where(t => t.InheritFromTemplateId == templateId).ToListAsync(ct);
        foreach (var child in inherited)
            child.InheritFromTemplateId = null;
        _db.ForumFlairTemplates.Remove(template);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AssignFlairAsync(Guid forumId, Guid userId, Guid? templateId, CancellationToken ct = default)
    {
        if (templateId.HasValue)
        {
            var exists = await _db.ForumFlairTemplates.AnyAsync(t => t.Id == templateId && t.ForumId == forumId, ct);
            if (!exists) throw new InvalidOperationException("Flair template not found.");
        }

        var existing = await _db.ForumMemberFlairs.FirstOrDefaultAsync(f => f.ForumId == forumId && f.UserId == userId, ct);
        if (templateId is null)
        {
            if (existing is not null) _db.ForumMemberFlairs.Remove(existing);
        }
        else if (existing is null)
        {
            _db.ForumMemberFlairs.Add(new ForumMemberFlair { ForumId = forumId, UserId = userId, TemplateId = templateId });
        }
        else
        {
            existing.TemplateId = templateId;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task SetAliasAsync(Guid forumId, Guid userId, string alias, CancellationToken ct = default)
    {
        var forum = await _db.Forums.FindAsync([forumId], ct)
            ?? throw new InvalidOperationException("Forum not found.");
        if (!forum.AllowAliases)
            throw new InvalidOperationException("Aliases are not allowed in this forum.");

        var trimmed = alias.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new InvalidOperationException("Alias cannot be empty.");

        var existing = await _db.ForumMemberAliases.FirstOrDefaultAsync(a => a.ForumId == forumId && a.UserId == userId, ct);
        if (existing is null)
            _db.ForumMemberAliases.Add(new ForumMemberAlias { ForumId = forumId, UserId = userId, Alias = trimmed });
        else
            existing.Alias = trimmed;

        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAliasAsync(Guid forumId, Guid userId, CancellationToken ct = default)
    {
        var existing = await _db.ForumMemberAliases.FirstOrDefaultAsync(a => a.ForumId == forumId && a.UserId == userId, ct);
        if (existing is not null)
        {
            _db.ForumMemberAliases.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task UpdateForumSettingsAsync(Guid forumId, UpdateForumSettingsRequest request, Guid? actorUserId = null, CancellationToken ct = default)
    {
        var forum = await _db.Forums.FindAsync([forumId], ct)
            ?? throw new InvalidOperationException("Forum not found.");

        var before = ForumSettingsSnapshot(forum);
        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Forum name is required.");
            forum.Name = name;
        }
        if (request.Slug is not null)
            await ApplySlugAsync(forum, request.Slug, ct);
        if (request.Description is not null) forum.Description = request.Description.Trim();
        if (request.AllowAliases.HasValue) forum.AllowAliases = request.AllowAliases.Value;
        if (request.NsfwAgeCheckRequired.HasValue) forum.NsfwAgeCheckRequired = request.NsfwAgeCheckRequired.Value;
        if (request.NsfwAgeCheckExplanation is not null)
            forum.NsfwAgeCheckExplanation = NsfwAge.NormalizeExplanation(request.NsfwAgeCheckExplanation);
        if (request.ClearAvatar) forum.AvatarMediaAssetId = null;
        else if (request.AvatarMediaId.HasValue)
            forum.AvatarMediaAssetId = await RequireUploadedAssetAsync(actorUserId, request.AvatarMediaId.Value, ct);
        if (request.ClearBanner) forum.BannerMediaAssetId = null;
        else if (request.BannerMediaId.HasValue)
            forum.BannerMediaAssetId = await RequireUploadedAssetAsync(actorUserId, request.BannerMediaId.Value, ct);
        forum.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        if (_changeLog is not null)
        {
            await _changeLog.RecordAsync(
                ChangeLogScopes.Forum,
                forumId,
                null,
                ChangeLogEntityTypes.Forum,
                forum.Id,
                ChangeLogActions.Updated,
                actorUserId,
                $"Updated forum settings for {forum.Name}",
                before,
                ForumSettingsSnapshot(forum),
                ct);
        }
    }

    private static object ForumSettingsSnapshot(Forum forum) => new
    {
        name = forum.Name,
        slug = forum.Slug,
        description = forum.Description,
        allowAliases = forum.AllowAliases,
        nsfwAgeCheckRequired = forum.NsfwAgeCheckRequired,
        nsfwAgeCheckExplanation = forum.NsfwAgeCheckExplanation,
        avatarMediaAssetId = forum.AvatarMediaAssetId,
        bannerMediaAssetId = forum.BannerMediaAssetId
    };

    private async Task ApplySlugAsync(Forum forum, string requested, CancellationToken ct)
    {
        var slug = requested.Trim().ToLowerInvariant();
        if (slug.Length == 0 || slug.Length > 64 || !ForumSlugPattern.IsMatch(slug))
            throw new InvalidOperationException("Use a slug of lowercase letters, numbers, and single hyphens (64 characters max).");
        if (slug == "new")
            throw new InvalidOperationException("That slug is reserved.");
        if (slug == forum.Slug) return;

        var taken = await _db.Forums.AnyAsync(
            f => f.Id != forum.Id && f.ParentForumId == forum.ParentForumId && f.Slug == slug,
            ct);
        if (taken)
            throw new InvalidOperationException("Forum slug already exists.");

        forum.Slug = slug;
    }

    private async Task<Guid> RequireUploadedAssetAsync(Guid? actorUserId, Guid mediaAssetId, CancellationToken ct)
    {
        var asset = await _db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == mediaAssetId, ct)
            ?? throw new InvalidOperationException("Media asset not found.");
        if (actorUserId.HasValue && asset.UploaderUserId != actorUserId.Value)
            throw new InvalidOperationException("You can only use media you uploaded.");
        return asset.Id;
    }
}
