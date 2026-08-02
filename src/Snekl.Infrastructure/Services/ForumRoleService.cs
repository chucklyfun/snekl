using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IForumRoleService
{
    Task<IReadOnlyList<ForumRoleDto>> ListRolesAsync(Guid forumId, CancellationToken ct = default);
    Task<ForumRoleDto> CreateRoleAsync(Guid forumId, CreateForumRoleRequest request, CancellationToken ct = default);
    Task<ForumRoleDto> UpdateRoleAsync(Guid forumId, Guid roleId, UpdateForumRoleRequest request, CancellationToken ct = default);
    Task DeleteRoleAsync(Guid forumId, Guid roleId, CancellationToken ct = default);
    Task<IReadOnlyList<ForumMemberDto>> ListMembersAsync(Guid forumId, CancellationToken ct = default);
    Task AssignRolesAsync(Guid forumId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default);
}

public class ForumRoleService : IForumRoleService
{
    private readonly SneklDbContext _db;

    public ForumRoleService(SneklDbContext db) => _db = db;

    public async Task<IReadOnlyList<ForumRoleDto>> ListRolesAsync(Guid forumId, CancellationToken ct = default)
    {
        var roles = await _db.ForumRoles
            .Where(r => r.ForumId == forumId)
            .OrderBy(r => r.Position)
            .ToListAsync(ct);
        return roles.Select(ToDto).ToList();
    }

    public async Task<ForumRoleDto> CreateRoleAsync(Guid forumId, CreateForumRoleRequest request, CancellationToken ct = default)
    {
        if (request.Name.Trim().Equals("@everyone", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot create a role named @everyone.");

        var role = new ForumRole
        {
            ForumId = forumId,
            Name = request.Name.Trim(),
            Color = request.Color,
            Position = request.Position,
            Permissions = PermissionParsing.ParseForumPermissions(request.Permissions)
        };
        _db.ForumRoles.Add(role);
        await _db.SaveChangesAsync(ct);
        return ToDto(role);
    }

    public async Task<ForumRoleDto> UpdateRoleAsync(Guid forumId, Guid roleId, UpdateForumRoleRequest request, CancellationToken ct = default)
    {
        var role = await _db.ForumRoles.FirstOrDefaultAsync(r => r.Id == roleId && r.ForumId == forumId, ct)
            ?? throw new InvalidOperationException("Role not found.");

        if (role.IsEveryone)
            role.Permissions = PermissionParsing.ParseForumPermissions(request.Permissions);
        else
        {
            role.Name = request.Name.Trim();
            role.Color = request.Color;
            role.Position = request.Position;
            role.Permissions = PermissionParsing.ParseForumPermissions(request.Permissions);
        }

        await _db.SaveChangesAsync(ct);
        return ToDto(role);
    }

    public async Task DeleteRoleAsync(Guid forumId, Guid roleId, CancellationToken ct = default)
    {
        var role = await _db.ForumRoles.FirstOrDefaultAsync(r => r.Id == roleId && r.ForumId == forumId, ct)
            ?? throw new InvalidOperationException("Role not found.");
        if (role.IsEveryone)
            throw new InvalidOperationException("Cannot delete @everyone role.");

        _db.ForumRoles.Remove(role);
        await _db.SaveChangesAsync(ct);
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

    public async Task AssignRolesAsync(Guid forumId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
    {
        _ = await _db.Users.FindAsync([userId], ct)
            ?? throw new InvalidOperationException("User not found.");

        var validRoleIds = await _db.ForumRoles
            .Where(r => r.ForumId == forumId && roleIds.Contains(r.Id) && !r.IsEveryone)
            .Select(r => r.Id)
            .ToListAsync(ct);

        var existing = await _db.ForumRoleAssignments
            .Where(a => a.ForumId == forumId && a.UserId == userId)
            .ToListAsync(ct);
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
    }

    private static ForumRoleDto ToDto(ForumRole role) => new(
        role.Id,
        role.ForumId,
        role.Name,
        role.Color,
        role.Position,
        PermissionNames.ToNames(role.Permissions),
        role.IsEveryone,
        role.InheritFromRoleId);

    private static ForumFlairTemplateDto ToFlairDto(ForumFlairTemplate t) => new(
        t.Id, t.ForumId, t.Emoji, t.IconUrl, t.Label, t.NameColor, t.NameStyle, t.Position, t.InheritFromTemplateId);
}

public interface IForumMemberService
{
    Task<IReadOnlyList<ForumFlairTemplateDto>> ListFlairTemplatesAsync(Guid forumId, CancellationToken ct = default);
    Task<ForumFlairTemplateDto> CreateFlairTemplateAsync(Guid forumId, CreateForumFlairTemplateRequest request, CancellationToken ct = default);
    Task DeleteFlairTemplateAsync(Guid forumId, Guid templateId, CancellationToken ct = default);
    Task AssignFlairAsync(Guid forumId, Guid userId, Guid? templateId, CancellationToken ct = default);
    Task SetAliasAsync(Guid forumId, Guid userId, string alias, CancellationToken ct = default);
    Task RemoveAliasAsync(Guid forumId, Guid userId, CancellationToken ct = default);
    Task UpdateForumSettingsAsync(Guid forumId, UpdateForumSettingsRequest request, CancellationToken ct = default);
}

public class ForumMemberService : IForumMemberService
{
    private readonly SneklDbContext _db;

    public ForumMemberService(SneklDbContext db) => _db = db;

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

    public async Task DeleteFlairTemplateAsync(Guid forumId, Guid templateId, CancellationToken ct = default)
    {
        var template = await _db.ForumFlairTemplates.FirstOrDefaultAsync(t => t.Id == templateId && t.ForumId == forumId, ct)
            ?? throw new InvalidOperationException("Flair template not found.");
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

    public async Task UpdateForumSettingsAsync(Guid forumId, UpdateForumSettingsRequest request, CancellationToken ct = default)
    {
        var forum = await _db.Forums.FindAsync([forumId], ct)
            ?? throw new InvalidOperationException("Forum not found.");

        if (request.Name is not null) forum.Name = request.Name.Trim();
        if (request.Description is not null) forum.Description = request.Description.Trim();
        if (request.AllowAliases.HasValue) forum.AllowAliases = request.AllowAliases.Value;
        forum.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }
}
