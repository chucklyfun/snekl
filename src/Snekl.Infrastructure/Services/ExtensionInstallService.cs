using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Snekl.Domain;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;
using Snekl.Redis;

namespace Snekl.Infrastructure.Services;

public interface IExtensionInstallService
{
    Task<ExtensionInstallOptionsDto> GetOptionsAsync(Guid userId, string? clientId = null, CancellationToken ct = default);
    Task<ExtensionInstallDto> CreateAsync(Guid userId, CreateExtensionInstallRequest request, CancellationToken ct = default);
    Task<ExtensionInstallDto?> GetAsync(Guid userId, Guid installId, CancellationToken ct = default);
    Task<IReadOnlyList<ExtensionInstallDto>> ListActiveAsync(CancellationToken ct = default);
    Task RevokeAsync(Guid installId, CancellationToken ct = default);
}

public class ExtensionInstallService : IExtensionInstallService
{
    private readonly SneklDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly RateLimitOptions _rateLimits;
    private readonly IConfiguration _config;

    public ExtensionInstallService(
        SneklDbContext db,
        IPermissionService permissions,
        IOptions<RateLimitOptions> rateLimits,
        IConfiguration config)
    {
        _db = db;
        _permissions = permissions;
        _rateLimits = rateLimits.Value;
        _config = config;
    }

    public async Task<ExtensionInstallOptionsDto> GetOptionsAsync(
        Guid userId,
        string? clientId = null,
        CancellationToken ct = default)
    {
        var canServer = await CanInstallServerAsync(userId, ct);
        var serverPerms = PermissionNames.ToNames(await _permissions.GetServerPermissionsAsync(userId, ct));
        var forums = await _db.Forums.AsNoTracking().OrderBy(f => f.Name).ToListAsync(ct);
        var rows = new List<ExtensionInstallOptionForumDto>();
        foreach (var forum in forums)
        {
            var can = await CanInstallForumAsync(userId, forum.Id, ct);
            var forumPerms = PermissionNames.ToNames(await _permissions.GetForumPermissionsAsync(userId, forum.Id, ct));
            rows.Add(new ExtensionInstallOptionForumDto(forum.Id, forum.Name, forum.Slug, can, forumPerms));
        }

        ExtensionInstallDto? existing = null;
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            var row = await FindActiveByClientIdAsync(clientId, ct);
            if (row is not null)
                existing = await ToDtoAsync(row, ct);
        }

        return new ExtensionInstallOptionsDto(canServer, serverPerms, rows, existing);
    }

    public async Task<ExtensionInstallDto> CreateAsync(Guid userId, CreateExtensionInstallRequest request, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct)
            ?? throw new InvalidOperationException("User not found.");

        var resource = request.ResourceType.Trim().ToLowerInvariant();
        var requestedServer = PermissionClip.ParseServer(request.ServerPermissions);
        var requestedForum = PermissionClip.ParseForum(request.ForumPermissions);
        var authorizerServer = await _permissions.GetServerPermissionsAsync(userId, ct);
        ServerPermissions serverGrant;
        ForumPermissions forumGrant;
        Guid? forumId = null;

        if (resource == ExtensionResourceTypes.Server)
        {
            if (!await CanInstallServerAsync(userId, ct))
                throw new PermissionDeniedException(nameof(ServerPermissions.ManageServerMembers), "server");
            serverGrant = PermissionClip.Intersect(requestedServer, authorizerServer);
            if (!serverGrant.HasFlag(ServerPermissions.ViewServer) && authorizerServer.HasFlag(ServerPermissions.ViewServer))
                serverGrant |= ServerPermissions.ViewServer;
            var allForum = ForumPermissions.None;
            foreach (var forum in await _db.Forums.AsNoTracking().ToListAsync(ct))
            {
                var have = await _permissions.GetForumPermissionsAsync(userId, forum.Id, ct);
                allForum |= have;
            }

            if (!await _db.Forums.AnyAsync(ct))
                allForum = await SuperOrEmptyForumPermsAsync(userId, requestedForum, ct);

            forumGrant = PermissionClip.Intersect(requestedForum, allForum);
        }
        else if (resource == ExtensionResourceTypes.Forum)
        {
            if (!request.ForumId.HasValue)
                throw new InvalidOperationException("Nest id is required for a nest install.");
            forumId = request.ForumId;
            if (!await CanInstallForumAsync(userId, forumId.Value, ct))
                throw new PermissionDeniedException(nameof(ForumPermissions.ManageForum), "forum");
            var authorizerForum = await _permissions.GetForumPermissionsAsync(userId, forumId.Value, ct);
            serverGrant = ServerPermissions.ViewServer & authorizerServer;
            forumGrant = PermissionClip.Intersect(requestedForum, authorizerForum);
        }
        else
        {
            throw new InvalidOperationException("Resource must be 'server' or 'forum'.");
        }

        if (forumGrant == ForumPermissions.None && !serverGrant.HasFlag(ServerPermissions.CreateForum))
            throw new InvalidOperationException("That grant would not allow the app to do anything you can do.");

        var clientId = request.ClientId.Trim();
        var appName = string.IsNullOrWhiteSpace(request.AppName) ? clientId : request.AppName.Trim();
        var existing = await FindActiveByClientIdAsync(clientId, ct);
        if (existing is not null)
        {
            existing.AppName = appName;
            existing.AdminUrl = EmptyToNull(request.AdminUrl);
            existing.IframeUrl = EmptyToNull(request.IframeUrl);
            existing.InstalledByUserId = userId;
            existing.InstalledByDid = user.Did ?? "";
            await _db.SaveChangesAsync(ct);
            return await ToDtoAsync(existing, ct);
        }

        var install = new ExtensionInstall
        {
            ClientId = clientId,
            AppName = appName,
            AdminUrl = EmptyToNull(request.AdminUrl),
            IframeUrl = EmptyToNull(request.IframeUrl),
            InstalledByUserId = userId,
            InstalledByDid = user.Did ?? "",
            ResourceType = resource,
            ForumId = forumId,
            ServerPermissions = serverGrant,
            ForumPermissions = forumGrant
        };
        _db.ExtensionInstalls.Add(install);
        await _db.SaveChangesAsync(ct);
        return await ToDtoAsync(install, ct);
    }

    public async Task<ExtensionInstallDto?> GetAsync(Guid userId, Guid installId, CancellationToken ct = default)
    {
        var install = await _db.ExtensionInstalls.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == installId && i.RevokedAt == null, ct);
        if (install is null) return null;
        if (install.InstalledByUserId != userId && !await CanAuthorizeInstallAsync(userId, install, ct))
            return null;
        return await ToDtoAsync(install, ct);
    }

    public async Task<IReadOnlyList<ExtensionInstallDto>> ListActiveAsync(CancellationToken ct = default)
    {
        var rows = await _db.ExtensionInstalls.AsNoTracking()
            .Where(i => i.RevokedAt == null)
            .OrderBy(i => i.AppName)
            .ThenByDescending(i => i.CreatedAt)
            .ToListAsync(ct);
        var unique = rows
            .GroupBy(i => i.ClientId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id).First())
            .OrderBy(i => i.AppName)
            .ToList();
        var result = new List<ExtensionInstallDto>();
        foreach (var row in unique)
            result.Add(await ToDtoAsync(row, ct));
        return result;
    }

    public async Task RevokeAsync(Guid installId, CancellationToken ct = default)
    {
        var install = await _db.ExtensionInstalls.FirstOrDefaultAsync(i => i.Id == installId, ct)
            ?? throw new InvalidOperationException("Install not found.");
        install.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<ExtensionInstall?> FindActiveByClientIdAsync(string clientId, CancellationToken ct) =>
        await _db.ExtensionInstalls
            .Where(i => i.RevokedAt == null && i.ClientId == clientId.Trim())
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .FirstOrDefaultAsync(ct);

    private async Task<bool> CanAuthorizeInstallAsync(Guid userId, ExtensionInstall install, CancellationToken ct)
    {
        if (string.Equals(install.ResourceType, ExtensionResourceTypes.Forum, StringComparison.OrdinalIgnoreCase)
            && install.ForumId.HasValue)
            return await CanInstallForumAsync(userId, install.ForumId.Value, ct);
        return await CanInstallServerAsync(userId, ct);
    }

    private async Task<bool> CanInstallServerAsync(Guid userId, CancellationToken ct)
    {
        if (await _permissions.IsSuperUserAsync(userId, ct)) return true;
        var perms = await _permissions.GetServerPermissionsAsync(userId, ct);
        return perms.HasFlag(ServerPermissions.ServerAdministrator)
            || perms.HasFlag(ServerPermissions.ManageServerMembers)
            || perms.HasFlag(ServerPermissions.ManageServerSettings);
    }

    private async Task<bool> CanInstallForumAsync(Guid userId, Guid forumId, CancellationToken ct)
    {
        if (await _permissions.IsSuperUserAsync(userId, ct)) return true;
        if (await _permissions.ViewerCanManageFromRolesAsync(userId, forumId, ct)) return true;
        var perms = await _permissions.GetForumPermissionsAsync(userId, forumId, ct);
        return perms.HasFlag(ForumPermissions.ForumAdministrator)
            || perms.HasFlag(ForumPermissions.ManageForum)
            || perms.HasFlag(ForumPermissions.ManageMembers);
    }

    private async Task<ForumPermissions> SuperOrEmptyForumPermsAsync(
        Guid userId,
        ForumPermissions requested,
        CancellationToken ct)
    {
        if (!await _permissions.IsSuperUserAsync(userId, ct))
            return ForumPermissions.None;
        return requested;
    }

    private async Task<ExtensionInstallDto> ToDtoAsync(ExtensionInstall install, CancellationToken ct)
    {
        string? forumName = null;
        if (install.ForumId.HasValue)
        {
            var forum = await _db.Forums.AsNoTracking().FirstOrDefaultAsync(f => f.Id == install.ForumId.Value, ct);
            forumName = forum?.Name;
        }

        return new ExtensionInstallDto(
            install.Id,
            install.ClientId,
            install.AppName,
            install.AdminUrl,
            install.IframeUrl,
            install.ResourceType,
            install.ForumId,
            forumName,
            PermissionNames.ToNames(install.ServerPermissions),
            PermissionNames.ToNames(install.ForumPermissions),
            install.InstalledByUserId,
            install.InstalledByDid,
            install.CreatedAt,
            RateLimitPolicyMapper.ToDto(_rateLimits, RedisExtensions.IsRedisConfigured(_config)));
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
