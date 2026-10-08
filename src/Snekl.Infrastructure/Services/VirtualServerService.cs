using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Domain.Identity;
using Snekl.Domain.Identity.Did;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IVirtualServerService
{
    Task<IReadOnlyList<VirtualServerSummary>> ListAsync(CancellationToken ct = default);

    Task<VirtualServerSummary> SpawnAsync(SpawnServerRequest request, CancellationToken ct = default);

    Task DisableAsync(Guid serverId, CancellationToken ct = default);

    Task EnableAsync(Guid serverId, CancellationToken ct = default);

    Task PurgeAsync(Guid serverId, CancellationToken ct = default);

    ServerTemplateDocument GetTemplate();

    void SaveTemplate(ServerTemplateDocument document);

    /// <summary>Pin an auth issuer the server must always trust (see TrustedAuthIssuer.IsRequired).</summary>
    Task PinAuthIssuerAsync(Guid serverId, string issuerUrl, CancellationToken ct = default);
}

/// <param name="RequiredAuthIssuerUrl">
/// Auth server the new server must always trust, e.g. the one its business owner signed up through.
/// </param>
public sealed record SpawnServerRequest(string Name, string OwnerUsername, string OwnerDid, string? RequiredAuthIssuerUrl = null);

public sealed record VirtualServerSummary(
    Guid Id,
    string Name,
    string Did,
    string BaseUrl,
    string? OwnerUsername,
    Guid? OwnerUserId,
    DateTime? DisabledAt,
    DateTime CreatedAt);

public sealed class VirtualServerService : IVirtualServerService
{
    private readonly SneklDbContext _db;
    private readonly IServerTemplateStore _templates;
    private readonly IConfiguration _config;

    public VirtualServerService(SneklDbContext db, IServerTemplateStore templates, IConfiguration config)
    {
        _db = db;
        _templates = templates;
        _config = config;
    }

    public async Task<IReadOnlyList<VirtualServerSummary>> ListAsync(CancellationToken ct = default)
    {
        var nodes = await _db.NodeConfig.AsNoTracking().OrderBy(n => n.CreatedAt).ToListAsync(ct);
        var ownerIds = nodes.Where(n => n.OwnerUserId.HasValue).Select(n => n.OwnerUserId!.Value).ToArray();
        var owners = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => ownerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        return nodes.Select(n => ToSummary(n, owners)).ToList();
    }

    public async Task<VirtualServerSummary> SpawnAsync(SpawnServerRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        var username = request.OwnerUsername.Trim().ToLowerInvariant();
        var did = request.OwnerDid.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Name is required.");
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("Owner username is required.");
        if (!did.StartsWith("did:", StringComparison.Ordinal))
            throw new InvalidOperationException("Owner DID is required.");

        var template = _templates.Read();
        var keys = DidKey.Generate();
        var baseUrl = (_config["Node:BaseUrl"] ?? "http://localhost:5001").TrimEnd('/');
        var server = new NodeConfig
        {
            Name = name,
            BaseUrl = baseUrl,
            Did = keys.Did,
            VerificationKeyMultibase = DidKey.PublicKeyMultibase(keys.Did),
            VerificationPrivateKeyPkcs8 = Convert.ToBase64String(keys.PrivateKey),
            // Host issuers and hubs are read live (see ServerTrust); only the template's own
            // entries are copied below.
            InheritHostAuthIssuers = template.InheritHostAuthIssuers,
            InheritHostHubs = template.InheritHostHubs
        };
        var owner = new User
        {
            ServerId = server.Id,
            Username = username,
            DisplayName = username,
            Email = $"{username}@virtual.snekl",
            Did = did,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
            Bio = ""
        };
        server.OwnerUserId = owner.Id;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.NodeConfig.Add(server);
        _db.Users.Add(owner);

        foreach (var issuer in template.AuthIssuers)
        {
            var issuerUrl = issuer.IssuerUrl.Trim().TrimEnd('/');
            var jwksUrl = issuer.JwksUrl.Trim();
            if (string.IsNullOrWhiteSpace(issuerUrl) || string.IsNullOrWhiteSpace(jwksUrl))
                continue;
            _db.TrustedAuthIssuers.Add(new TrustedAuthIssuer
            {
                ServerId = server.Id,
                Name = string.IsNullOrWhiteSpace(issuer.Name) ? issuerUrl : issuer.Name.Trim(),
                IssuerUrl = issuerUrl,
                JwksUrl = jwksUrl,
                IsActive = true
            });
        }

        if (!string.IsNullOrWhiteSpace(request.RequiredAuthIssuerUrl))
            await PinIssuerAsync(server.Id, request.RequiredAuthIssuerUrl, ct);

        var hubUrl = template.Hub.BaseUrl.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(hubUrl))
        {
            _db.HubInvites.Add(new HubInvite
            {
                ServerId = server.Id,
                HubBaseUrl = hubUrl,
                HubName = string.IsNullOrWhiteSpace(template.Hub.Name) ? hubUrl : template.Hub.Name.Trim(),
                RequestedByDid = server.Did,
                RequestedByUsername = username,
                Status = "accepted"
            });
        }

        foreach (var role in template.Roles)
        {
            if (string.IsNullOrWhiteSpace(role.Name))
                continue;
            _db.ServerRoles.Add(new ServerRole
            {
                ServerId = server.Id,
                Name = role.Name.Trim(),
                Color = role.Color,
                Position = role.Position,
                IsEveryone = role.IsEveryone,
                Modes = new Dictionary<string, GrantMode>(role.Modes, StringComparer.OrdinalIgnoreCase)
            });
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToSummary(server, new Dictionary<Guid, string> { [owner.Id] = owner.Username });
    }

    public async Task DisableAsync(Guid serverId, CancellationToken ct = default)
    {
        var node = await RequireAsync(serverId, ct);
        if (node.DisabledAt is null)
        {
            node.DisabledAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task EnableAsync(Guid serverId, CancellationToken ct = default)
    {
        var node = await RequireAsync(serverId, ct);
        if (node.DisabledAt is not null)
        {
            node.DisabledAt = null;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task PurgeAsync(Guid serverId, CancellationToken ct = default)
    {
        var node = await RequireAsync(serverId, ct);
        if (node.DisabledAt is null)
            throw new InvalidOperationException("Disable the server before purging it.");

        node.OwnerUserId = null;
        await _db.SaveChangesAsync(ct);

        await DeleteScopedAsync<ReplyAlert>(serverId, ct);
        await DeleteScopedAsync<PushDevice>(serverId, ct);
        await DeleteScopedAsync<ThreadAlertPreference>(serverId, ct);
        await DeleteScopedAsync<PlaceAlertPreference>(serverId, ct);
        await DeleteScopedAsync<DirectMessage>(serverId, ct);
        await DeleteScopedAsync<DirectMessageForumPreference>(serverId, ct);
        await DeleteScopedAsync<DirectConversation>(serverId, ct);
        await DeleteScopedAsync<DirectConversationRequest>(serverId, ct);
        await DeleteScopedAsync<PostMedia>(serverId, ct);
        await DeleteScopedAsync<PostReaction>(serverId, ct);
        await DeleteScopedAsync<PostRevision>(serverId, ct);
        await DeleteScopedAsync<PostUpvote>(serverId, ct);
        await DeleteScopedAsync<PostReport>(serverId, ct);
        await DeleteScopedAsync<Post>(serverId, ct);
        await DeleteScopedAsync<ForumPage>(serverId, ct);
        await DeleteScopedAsync<ForumContentBlock>(serverId, ct);
        await DeleteScopedAsync<ForumMemberFlair>(serverId, ct);
        await DeleteScopedAsync<ForumMemberAlias>(serverId, ct);
        await DeleteScopedAsync<ForumMemberBan>(serverId, ct);
        await DeleteScopedAsync<ForumMembership>(serverId, ct);
        await DeleteScopedAsync<ForumRoleAssignment>(serverId, ct);
        await DeleteScopedAsync<ForumFlairTemplate>(serverId, ct);
        await DeleteScopedAsync<ForumRole>(serverId, ct);
        await DeleteScopedAsync<WatercoolerRoleAssignment>(serverId, ct);
        await DeleteScopedAsync<WatercoolerRole>(serverId, ct);
        await DeleteScopedAsync<Watercooler>(serverId, ct);
        await DeleteScopedAsync<Emote>(serverId, ct);
        await DeleteScopedAsync<MediaAsset>(serverId, ct);
        await DeleteScopedAsync<ListEntryIgnore>(serverId, ct);
        await DeleteScopedAsync<ListEntry>(serverId, ct);
        await DeleteScopedAsync<ListSubscription>(serverId, ct);
        await DeleteScopedAsync<CuratedList>(serverId, ct);
        await DeleteScopedAsync<ChangeLogEntry>(serverId, ct);
        await DeleteScopedAsync<HubOutboxMessage>(serverId, ct);
        await DeleteScopedAsync<ExtensionInstall>(serverId, ct);
        await DeleteScopedAsync<ServerRoleAssignment>(serverId, ct);
        await DeleteScopedAsync<ServerRole>(serverId, ct);
        await DeleteScopedAsync<UserStatus>(serverId, ct);
        await DeleteScopedAsync<SuperUserGrant>(serverId, ct);
        await DeleteScopedAsync<HubInvite>(serverId, ct);
        await DeleteScopedAsync<PeerNode>(serverId, ct);
        await DeleteScopedAsync<TrustedAuthIssuer>(serverId, ct);
        await DeleteScopedAsync<Forum>(serverId, ct);
        await DeleteScopedAsync<User>(serverId, ct);

        _db.NodeConfig.Remove(node);
        await _db.SaveChangesAsync(ct);
    }

    public ServerTemplateDocument GetTemplate() => _templates.Read();

    public void SaveTemplate(ServerTemplateDocument document)
    {
        document.AuthIssuers ??= [];
        document.Hub ??= new ServerTemplateHub();
        document.Roles ??= [];
        foreach (var role in document.Roles)
        {
            if (string.IsNullOrWhiteSpace(role.Name))
                throw new InvalidOperationException("Each template role needs a name.");
            role.Modes ??= new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var issuer in document.AuthIssuers)
        {
            if (string.IsNullOrWhiteSpace(issuer.IssuerUrl) || string.IsNullOrWhiteSpace(issuer.JwksUrl))
                throw new InvalidOperationException("Each template auth issuer needs an issuer URL and a JWKS URL.");
        }

        _templates.Write(document);
    }

    public async Task PinAuthIssuerAsync(Guid serverId, string issuerUrl, CancellationToken ct = default)
    {
        await PinIssuerAsync(serverId, issuerUrl, ct);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Marks the server's own row for an issuer as required, creating it from the host's active
    /// issuer of that URL (or the template's, already added) when the server has none.
    /// </summary>
    private async Task PinIssuerAsync(Guid serverId, string issuerUrl, CancellationToken ct)
    {
        var url = issuerUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Required auth issuer URL is empty.");

        var own = _db.TrustedAuthIssuers.Local.FirstOrDefault(i => i.ServerId == serverId && i.IssuerUrl == url)
            ?? await _db.TrustedAuthIssuers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.ServerId == serverId && i.IssuerUrl == url, ct);
        if (own is not null)
        {
            own.IsActive = true;
            own.IsRequired = true;
            return;
        }

        var hostId = await _db.NodeConfig.AsNoTracking()
            .OrderBy(n => n.CreatedAt)
            .Select(n => n.Id)
            .FirstOrDefaultAsync(ct);
        var source = await _db.TrustedAuthIssuers.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(i => i.ServerId == hostId && i.IssuerUrl == url && i.IsActive, ct)
            ?? throw new InvalidOperationException(
                $"This host does not trust the auth server {url}, so a server can't require it.");
        _db.TrustedAuthIssuers.Add(new TrustedAuthIssuer
        {
            ServerId = serverId,
            Name = source.Name,
            IssuerUrl = url,
            JwksUrl = source.JwksUrl,
            IsActive = true,
            IsRequired = true
        });
    }

    private async Task<NodeConfig> RequireAsync(Guid serverId, CancellationToken ct) =>
        await _db.NodeConfig.FirstOrDefaultAsync(n => n.Id == serverId, ct)
        ?? throw new InvalidOperationException("Server not found.");

    private async Task DeleteScopedAsync<TEntity>(Guid serverId, CancellationToken ct) where TEntity : class, IServerScoped
    {
        var rows = _db.Set<TEntity>().IgnoreQueryFilters().Where(e => e.ServerId == serverId);
        if (_db.Database.IsRelational())
        {
            await rows.ExecuteDeleteAsync(ct);
            return;
        }

        _db.RemoveRange(await rows.ToListAsync(ct));
        await _db.SaveChangesAsync(ct);
    }

    private static VirtualServerSummary ToSummary(NodeConfig node, IReadOnlyDictionary<Guid, string> owners)
    {
        string? owner = null;
        if (node.OwnerUserId is Guid ownerId)
            owners.TryGetValue(ownerId, out owner);
        return new VirtualServerSummary(
            node.Id,
            node.Name,
            node.Did,
            node.BaseUrl,
            owner,
            node.OwnerUserId,
            node.DisabledAt,
            node.CreatedAt);
    }
}
