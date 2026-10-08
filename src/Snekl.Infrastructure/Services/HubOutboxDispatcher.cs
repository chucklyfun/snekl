using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Snekl.Domain.Dtos;
using Snekl.Domain.Security;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public sealed class HubOutboxDispatcher : BackgroundService
{
    public const int MaxAttempts = 12;

    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<HubOutboxDispatcher> _logger;

    public HubOutboxDispatcher(
        IServiceScopeFactory scopes,
        IHttpClientFactory http,
        ILogger<HubOutboxDispatcher> logger)
    {
        _scopes = scopes;
        _http = http;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hub outbox drain failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task DrainOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SneklDbContext>();
        var nodeService = scope.ServiceProvider.GetRequiredService<INodeService>();
        var due = await db.HubOutbox
            .Where(m => m.DeliveredAt == null && m.NextAttemptAt <= DateTime.UtcNow && m.Attempts < MaxAttempts)
            .OrderBy(m => m.NextAttemptAt)
            .Take(20)
            .ToListAsync(ct);
        if (due.Count == 0) return;

        var node = await nodeService.GetOrCreateAsync(ct);
        var privateKey = nodeService.GetSigningPrivateKey(node);
        var client = _http.CreateClient();
        // Each message goes out as the server that queued it, so a spawned server sending to a
        // hub it inherits is never mistaken for the host. Unscoped messages stay with the host.
        var senderIds = due.Select(m => m.ServerId).Where(id => id != Guid.Empty && id != node.Id).Distinct().ToList();
        var senders = await db.NodeConfig.Where(n => senderIds.Contains(n.Id)).ToDictionaryAsync(n => n.Id, ct);

        foreach (var message in due)
        {
            message.Attempts++;
            try
            {
                using var payloadDoc = JsonDocument.Parse(message.PayloadJson);
                var payload = payloadDoc.RootElement.Clone();
                var timestamp = DateTime.UtcNow;
                var sender = senders.TryGetValue(message.ServerId, out var queuedBy) ? queuedBy : node;
                var signature = FederationEnvelopeSigner.Sign(
                    ReferenceEquals(sender, node) ? privateKey : nodeService.GetSigningPrivateKey(sender),
                    message.EnvelopeType,
                    sender.Id,
                    sender.BaseUrl,
                    timestamp,
                    payload);
                var envelope = new FederationEnvelope(
                    message.EnvelopeType,
                    sender.Id,
                    sender.BaseUrl,
                    timestamp,
                    payload,
                    signature);

                var response = await client.PostAsJsonAsync(
                    $"{message.HubBaseUrl.TrimEnd('/')}{message.Path}",
                    envelope,
                    HubFeedPublisher.JsonOptions,
                    ct);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {body}");
                }

                db.HubOutbox.Remove(message);
            }
            catch (Exception ex)
            {
                message.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                var delaySeconds = Math.Min(300, Math.Pow(2, Math.Min(message.Attempts, 8)));
                message.NextAttemptAt = DateTime.UtcNow.AddSeconds(delaySeconds);
                _logger.LogWarning(
                    ex,
                    "Hub outbox delivery failed for {Type} to {Hub} (attempt {Attempt})",
                    message.EnvelopeType,
                    message.HubBaseUrl,
                    message.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
