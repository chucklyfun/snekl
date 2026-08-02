using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Caching;
using Snekl.Infrastructure.Data;
using Snekl.Redis;

namespace Snekl.Infrastructure.Services;

public interface IFederationClient
{
    Task<NodeInfoDto?> FetchNodeInfoAsync(string baseUrl, CancellationToken ct = default);
    Task<bool> DeliverAsync(string baseUrl, FederationEnvelope envelope, CancellationToken ct = default);
}

public class FederationClient : IFederationClient
{
    private readonly HttpClient _http;
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public FederationClient(HttpClient http) => _http = http;

    public async Task<NodeInfoDto?> FetchNodeInfoAsync(string baseUrl, CancellationToken ct = default)
    {
        try
        {
            var url = $"{baseUrl.TrimEnd('/')}/api/node";
            return await _http.GetFromJsonAsync<NodeInfoDto>(url, JsonOptions, ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> DeliverAsync(string baseUrl, FederationEnvelope envelope, CancellationToken ct = default)
    {
        try
        {
            var url = $"{baseUrl.TrimEnd('/')}/api/federation/inbox";
            var response = await _http.PostAsJsonAsync(url, envelope, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

public interface IFederationService
{
    Task<PeerDto> SubscribeAsync(string baseUrl, CancellationToken ct = default);
    Task<IReadOnlyList<PeerDto>> ListPeersAsync(CancellationToken ct = default);
    Task UnsubscribeAsync(Guid peerId, CancellationToken ct = default);
    Task BroadcastPostAsync(Post post, Forum forum, User author, CancellationToken ct = default);
    Task BroadcastForumAsync(Forum forum, User author, CancellationToken ct = default);
    Task ReceiveAsync(FederationEnvelope envelope, CancellationToken ct = default);
}

public class FederationService : IFederationService
{
    private readonly SneklDbContext _db;
    private readonly IFederationClient _client;
    private readonly INodeService _nodeService;
    private readonly IForumService _forumService;
    private readonly IPostService _postService;
    private readonly IRedisDedupService? _dedup;

    public FederationService(
        SneklDbContext db,
        IFederationClient client,
        INodeService nodeService,
        IForumService forumService,
        IPostService postService,
        IServiceProvider services)
    {
        _db = db;
        _client = client;
        _nodeService = nodeService;
        _forumService = forumService;
        _postService = postService;
        _dedup = services.GetService(typeof(IRedisDedupService)) as IRedisDedupService;
    }

    public async Task<PeerDto> SubscribeAsync(string baseUrl, CancellationToken ct = default)
    {
        baseUrl = baseUrl.Trim().TrimEnd('/');
        var localNode = await _nodeService.GetOrCreateAsync(ct);

        if (string.Equals(localNode.BaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot subscribe to yourself.");

        var remoteInfo = await _client.FetchNodeInfoAsync(baseUrl, ct)
            ?? throw new InvalidOperationException("Unable to reach remote node.");

        if (await _db.PeerNodes.AnyAsync(p => p.BaseUrl == baseUrl, ct))
            throw new InvalidOperationException("Already subscribed to this node.");

        var peer = new PeerNode
        {
            BaseUrl = baseUrl,
            Name = remoteInfo.Name,
            RemoteNodeId = remoteInfo.Id
        };

        _db.PeerNodes.Add(peer);
        await _db.SaveChangesAsync(ct);

        // Notify remote node of our subscription
        var envelope = new FederationEnvelope(
            "node.subscribe",
            localNode.Id,
            localNode.BaseUrl,
            DateTime.UtcNow,
            new NodeInfoDto(localNode.Id, localNode.Did, localNode.Name, localNode.BaseUrl, localNode.Description, localNode.CreatedAt));

        await _client.DeliverAsync(baseUrl, envelope, ct);

        return ToDto(peer);
    }

    public async Task<IReadOnlyList<PeerDto>> ListPeersAsync(CancellationToken ct = default)
    {
        var peers = await _db.PeerNodes.OrderBy(p => p.Name).ToListAsync(ct);
        return peers.Select(ToDto).ToList();
    }

    public async Task UnsubscribeAsync(Guid peerId, CancellationToken ct = default)
    {
        var peer = await _db.PeerNodes.FindAsync([peerId], ct)
            ?? throw new InvalidOperationException("Peer not found.");
        _db.PeerNodes.Remove(peer);
        await _db.SaveChangesAsync(ct);
    }

    public async Task BroadcastPostAsync(Post post, Forum forum, User author, CancellationToken ct = default)
    {
        var localNode = await _nodeService.GetOrCreateAsync(ct);
        var payload = new FederatedPostPayload(
            post.Id,
            forum.Id,
            forum.Slug,
            forum.Name,
            post.ParentPostId,
            new FederatedUserPayload(author.Id, author.Username, author.DisplayName, author.Bio, localNode.BaseUrl),
            post.Message,
            post.CreatedAt);

        var envelope = new FederationEnvelope("post.create", localNode.Id, localNode.BaseUrl, DateTime.UtcNow, payload);
        await DeliverToPeersAsync(envelope, ct);
    }

    public async Task BroadcastForumAsync(Forum forum, User author, CancellationToken ct = default)
    {
        var localNode = await _nodeService.GetOrCreateAsync(ct);
        var payload = new FederatedForumPayload(
            forum.Id,
            forum.Name,
            forum.Slug,
            forum.Description,
            new FederatedUserPayload(author.Id, author.Username, author.DisplayName, author.Bio, localNode.BaseUrl),
            forum.CreatedAt);

        var envelope = new FederationEnvelope("forum.create", localNode.Id, localNode.BaseUrl, DateTime.UtcNow, payload);
        await DeliverToPeersAsync(envelope, ct);
    }

    public async Task ReceiveAsync(FederationEnvelope envelope, CancellationToken ct = default)
    {
        if (_dedup is not null && TryGetEntityId(envelope, out var entityId))
        {
            var dedupKey = CacheKeys.FederationEnvelope(envelope.SourceNodeId, envelope.Type, entityId);
            if (!await _dedup.TryMarkProcessedAsync(dedupKey, TimeSpan.FromDays(1), ct))
                return;
        }

        var json = JsonSerializer.Serialize(envelope.Payload);
        switch (envelope.Type)
        {
            case "post.create":
                var postPayload = JsonSerializer.Deserialize<FederatedPostPayload>(json, FederationClient.JsonOptions)!;
                await _postService.UpsertFederatedAsync(postPayload, envelope.SourceNodeId, envelope.SourceNodeUrl, ct);
                break;
            case "forum.create":
                var forumPayload = JsonSerializer.Deserialize<FederatedForumPayload>(json, FederationClient.JsonOptions)!;
                await _forumService.UpsertFederatedAsync(forumPayload, envelope.SourceNodeId, envelope.SourceNodeUrl, ct);
                break;
            case "node.subscribe":
                var nodeInfo = JsonSerializer.Deserialize<NodeInfoDto>(json, FederationClient.JsonOptions)!;
                await UpsertPeerFromSubscriptionAsync(nodeInfo, ct);
                break;
        }
    }

    private static bool TryGetEntityId(FederationEnvelope envelope, out Guid entityId)
    {
        entityId = Guid.Empty;
        try
        {
            var json = JsonSerializer.Serialize(envelope.Payload);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("id", out var idProp) &&
                   Guid.TryParse(idProp.GetString(), out entityId);
        }
        catch
        {
            return false;
        }
    }

    private async Task UpsertPeerFromSubscriptionAsync(NodeInfoDto nodeInfo, CancellationToken ct)
    {
        var existing = await _db.PeerNodes.FirstOrDefaultAsync(p => p.BaseUrl == nodeInfo.BaseUrl, ct);
        if (existing is not null) return;

        _db.PeerNodes.Add(new PeerNode
        {
            BaseUrl = nodeInfo.BaseUrl,
            Name = nodeInfo.Name,
            RemoteNodeId = nodeInfo.Id
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task DeliverToPeersAsync(FederationEnvelope envelope, CancellationToken ct)
    {
        var peers = await _db.PeerNodes.Where(p => p.IsActive).ToListAsync(ct);
        foreach (var peer in peers)
        {
            var delivered = await _client.DeliverAsync(peer.BaseUrl, envelope, ct);
            if (delivered) peer.LastSyncedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    private static PeerDto ToDto(PeerNode peer) => new(
        peer.Id, peer.BaseUrl, peer.Name, peer.RemoteNodeId, peer.IsActive, peer.SubscribedAt);
}
