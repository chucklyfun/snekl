using Snekl.Hub.Infrastructure.Services;

namespace Snekl.Hub;

public sealed class HubFeedOriginSyncHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<HubFeedOriginSyncHostedService> _logger;

    public HubFeedOriginSyncHostedService(
        IServiceScopeFactory scopes,
        IConfiguration config,
        ILogger<HubFeedOriginSyncHostedService> logger)
    {
        _scopes = scopes;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var onStart = _config.GetValue("Hub:SyncOriginsOnStart", true);
        var intervalSeconds = _config.GetValue("Hub:OriginSyncIntervalSeconds", 30);
        if (!onStart && intervalSeconds <= 0)
            return;

        if (onStart)
            await SyncOnceAsync(stoppingToken);

        if (intervalSeconds <= 0)
            return;

        var period = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 15, 3600));
        using var timer = new PeriodicTimer(period);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await SyncOnceAsync(stoppingToken);
    }

    private async Task SyncOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IHubFeedOriginSync>().SyncAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hub origin feed sync failed");
        }
    }
}
