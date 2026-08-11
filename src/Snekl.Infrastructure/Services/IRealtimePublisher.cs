using Snekl.Domain.Dtos;

namespace Snekl.Infrastructure.Services;

public interface IRealtimePublisher
{
    Task PublishPostEventAsync(RealtimePostEvent evt, CancellationToken ct = default);
}

/// <summary>
/// No-op publisher used until the API registers the SignalR implementation.
/// </summary>
public sealed class NullRealtimePublisher : IRealtimePublisher
{
    public Task PublishPostEventAsync(RealtimePostEvent evt, CancellationToken ct = default) =>
        Task.CompletedTask;
}
