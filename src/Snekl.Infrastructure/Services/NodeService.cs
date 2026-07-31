using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface INodeService
{
    Task<NodeConfig> GetOrCreateAsync(CancellationToken ct = default);
    Task<NodeConfig> UpdateAsync(string name, string baseUrl, string description, CancellationToken ct = default);
}

public class NodeService : INodeService
{
    private readonly SneklDbContext _db;

    public NodeService(SneklDbContext db) => _db = db;

    public async Task<NodeConfig> GetOrCreateAsync(CancellationToken ct = default)
    {
        var node = await _db.NodeConfig.FirstOrDefaultAsync(ct);
        if (node is not null) return node;

        node = new NodeConfig();
        _db.NodeConfig.Add(node);
        await _db.SaveChangesAsync(ct);
        return node;
    }

    public async Task<NodeConfig> UpdateAsync(string name, string baseUrl, string description, CancellationToken ct = default)
    {
        var node = await GetOrCreateAsync(ct);
        node.Name = name;
        node.BaseUrl = baseUrl.TrimEnd('/');
        node.Description = description;
        await _db.SaveChangesAsync(ct);
        return node;
    }
}
