using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Snekl.Hub.Domain.Dtos;

namespace Snekl.Hub.Infrastructure.Services;

public interface IHubFeedOriginSync
{
    Task<HubFeedSyncResultDto> SyncAsync(CancellationToken ct = default, int? maxAttempts = null);
}

public class HubFeedOriginSync : IHubFeedOriginSync
{
    private readonly IFederationRegistry _registry;
    private readonly IHubFeedService _feed;
    private readonly IOriginRecentClient _origins;
    private readonly IConfiguration _config;
    private readonly ILogger<HubFeedOriginSync> _logger;

    public HubFeedOriginSync(
        IFederationRegistry registry,
        IHubFeedService feed,
        IOriginRecentClient origins,
        IConfiguration config,
        ILogger<HubFeedOriginSync> logger)
    {
        _registry = registry;
        _feed = feed;
        _origins = origins;
        _config = config;
        _logger = logger;
    }

    public async Task<HubFeedSyncResultDto> SyncAsync(CancellationToken ct = default, int? maxAttempts = null)
    {
        await _feed.ReindexAsync(ct);
        var servers = await _registry.ListServersAsync(ct);
        if (servers.Count == 0)
            return new HubFeedSyncResultDto(0, 0, []);

        var mapVersion = await _registry.GetMapVersionAsync(ct);
        var limit = Math.Clamp(_config.GetValue("Hub:OriginSyncLimit", 50), 1, 100);
        var retries = Math.Clamp(maxAttempts ?? _config.GetValue("Hub:OriginSyncRetries", 3), 1, 8);
        var origins = new List<HubFeedOriginSyncServerDto>();
        var totalIngested = 0;

        foreach (var server in servers)
        {
            var pulled = false;
            string? error = null;
            var ingested = 0;
            for (var attempt = 1; attempt <= retries && !pulled && !ct.IsCancellationRequested; attempt++)
            {
                try
                {
                    var threads = await _origins.GetRecentAsync(server.BaseUrl, limit, ct);
                    var eventAt = DateTime.UtcNow;
                    ingested = 0;
                    foreach (var thread in threads)
                    {
                        var card = HubFeedOriginMapper.ToCard(
                            thread,
                            server.ServerDid,
                            server.BaseUrl,
                            mapVersion,
                            eventAt);
                        if (card is null) continue;
                        await _feed.IngestCardAsync(card, ct);
                        ingested++;
                    }

                    _logger.LogInformation(
                        "Hub feed pull ingested {Count} public threads from {Did} ({Url})",
                        ingested,
                        server.ServerDid,
                        server.BaseUrl);
                    pulled = true;
                    error = null;
                }
                catch (Exception ex) when (attempt < retries && !ct.IsCancellationRequested)
                {
                    error = ex.Message;
                    _logger.LogWarning(
                        ex,
                        "Hub feed pull failed for {Did} ({Url}), retry {Attempt}/{Retries}",
                        server.ServerDid,
                        server.BaseUrl,
                        attempt,
                        retries);
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    _logger.LogWarning(
                        ex,
                        "Hub feed pull failed for {Did} ({Url})",
                        server.ServerDid,
                        server.BaseUrl);
                }
            }

            totalIngested += ingested;
            origins.Add(new HubFeedOriginSyncServerDto(
                server.ServerDid,
                server.BaseUrl,
                server.Name,
                ingested,
                pulled ? null : error));
        }

        return new HubFeedSyncResultDto(servers.Count, totalIngested, origins);
    }
}
