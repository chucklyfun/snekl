using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Domain.Identity.Did;
using Snekl.Infrastructure.Data;
using Snekl.Redis;

namespace Snekl.Infrastructure.Services;

public interface INodeService
{
    Task<NodeConfig> GetOrCreateAsync(CancellationToken ct = default);
    Task<NodeInfoDto> GetInfoAsync(CancellationToken ct = default);
    Task<NodeConfig> UpdateAsync(string name, string baseUrl, string description, CancellationToken ct = default);
    Task<int> GetVoteAlertBaseAsync(CancellationToken ct = default);
    Task<int> SetVoteAlertBaseAsync(int value, CancellationToken ct = default);
    byte[] GetSigningPrivateKey(NodeConfig node);
    string GetVerificationMethodId(NodeConfig node);
}

public class NodeService : INodeService
{
    private readonly SneklDbContext _db;
    private readonly RateLimitOptions _rateLimits;
    private readonly IConfiguration _config;
    private readonly ICurrentServer? _current;

    public NodeService(
        SneklDbContext db,
        IOptions<RateLimitOptions> rateLimits,
        IConfiguration config,
        ICurrentServer? current = null)
    {
        _db = db;
        _rateLimits = rateLimits.Value;
        _config = config;
        _current = current;
    }

    public async Task<NodeConfig> GetOrCreateAsync(CancellationToken ct = default)
    {
        if (_current?.ServerId is Guid serverId && serverId != Guid.Empty)
        {
            var selected = await _db.NodeConfig.FirstOrDefaultAsync(n => n.Id == serverId, ct)
                ?? throw new InvalidOperationException("Server not found.");
            await EnsureKeysAsync(selected, ct);
            return selected;
        }

        var node = await _db.NodeConfig.OrderBy(n => n.CreatedAt).FirstOrDefaultAsync(ct);
        if (node is not null)
        {
            await EnsureKeysAsync(node, ct);
            return node;
        }

        node = new NodeConfig();
        node.Did = DidWeb.FromBaseUrl(node.BaseUrl);
        var keys = DidKey.Generate();
        node.VerificationKeyMultibase = DidKey.PublicKeyMultibase(keys.Did);
        node.VerificationPrivateKeyPkcs8 = Convert.ToBase64String(keys.PrivateKey);
        _db.NodeConfig.Add(node);
        await _db.SaveChangesAsync(ct);
        return node;
    }

    public async Task<NodeInfoDto> GetInfoAsync(CancellationToken ct = default)
    {
        var node = await GetOrCreateAsync(ct);
        // Own accepted hubs first, then the host's for a spawned server that inherits them.
        var hubBaseUrl = (await _db.EffectiveAcceptedHubUrlsAsync(ct)).FirstOrDefault();
        string? ownerUsername = null;
        if (node.OwnerUserId is Guid ownerId)
        {
            ownerUsername = await _db.Users.AsNoTracking()
                .Where(u => u.Id == ownerId)
                .Select(u => u.Username)
                .FirstOrDefaultAsync(ct);
        }
        return new NodeInfoDto(
            node.Id,
            node.Did,
            node.Name,
            node.BaseUrl,
            node.Description,
            node.CreatedAt,
            string.IsNullOrWhiteSpace(hubBaseUrl) ? null : hubBaseUrl.TrimEnd('/'),
            RateLimitPolicyMapper.ToDto(_rateLimits, RedisExtensions.IsRedisConfigured(_config)),
            ownerUsername,
            node.OwnerUserId);
    }

    public async Task<NodeConfig> UpdateAsync(string name, string baseUrl, string description, CancellationToken ct = default)
    {
        var node = await GetOrCreateAsync(ct);
        node.Name = name;
        node.BaseUrl = baseUrl.TrimEnd('/');
        node.Did = DidWeb.FromBaseUrl(node.BaseUrl);
        node.Description = description;
        await _db.SaveChangesAsync(ct);
        return node;
    }

    public async Task<int> GetVoteAlertBaseAsync(CancellationToken ct = default)
    {
        var query = _db.NodeConfig.AsNoTracking();
        if (_current?.ServerId is Guid serverId && serverId != Guid.Empty)
            query = query.Where(n => n.Id == serverId);
        var value = await query.OrderBy(n => n.CreatedAt).Select(n => (int?)n.VoteAlertBase).FirstOrDefaultAsync(ct);
        return VoteAlertLevels.NormalizeBase(value ?? VoteAlertLevels.DefaultBase);
    }

    public async Task<int> SetVoteAlertBaseAsync(int value, CancellationToken ct = default)
    {
        VoteAlertLevels.RequireBase(value);
        var node = await GetOrCreateAsync(ct);
        node.VoteAlertBase = value;
        await _db.SaveChangesAsync(ct);
        return node.VoteAlertBase;
    }

    public byte[] GetSigningPrivateKey(NodeConfig node) =>
        Convert.FromBase64String(node.VerificationPrivateKeyPkcs8);

    public string GetVerificationMethodId(NodeConfig node)
    {
        var multibase = node.VerificationKeyMultibase;
        if (string.IsNullOrWhiteSpace(multibase))
            multibase = DidKey.PublicKeyMultibase(node.Did);
        return $"{node.Did}#{multibase[1..]}";
    }

    private async Task EnsureKeysAsync(NodeConfig node, CancellationToken ct)
    {
        var changed = false;
        if (string.IsNullOrEmpty(node.Did))
        {
            node.Did = DidWeb.FromBaseUrl(node.BaseUrl);
            changed = true;
        }

        if (string.IsNullOrEmpty(node.VerificationKeyMultibase) ||
            string.IsNullOrEmpty(node.VerificationPrivateKeyPkcs8))
        {
            var keys = DidKey.Generate();
            node.VerificationKeyMultibase = DidKey.PublicKeyMultibase(keys.Did);
            node.VerificationPrivateKeyPkcs8 = Convert.ToBase64String(keys.PrivateKey);
            changed = true;
        }

        if (changed)
            await _db.SaveChangesAsync(ct);
    }
}
