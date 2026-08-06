using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Entities;
using Snekl.Domain.Identity.Did;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface INodeService
{
    Task<NodeConfig> GetOrCreateAsync(CancellationToken ct = default);
    Task<NodeConfig> UpdateAsync(string name, string baseUrl, string description, CancellationToken ct = default);
    byte[] GetSigningPrivateKey(NodeConfig node);
    string GetVerificationMethodId(NodeConfig node);
}

public class NodeService : INodeService
{
    private readonly SneklDbContext _db;

    public NodeService(SneklDbContext db) => _db = db;

    public async Task<NodeConfig> GetOrCreateAsync(CancellationToken ct = default)
    {
        var node = await _db.NodeConfig.FirstOrDefaultAsync(ct);
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
