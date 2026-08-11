using Microsoft.AspNetCore.SignalR;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Realtime;

public sealed class SignalRRealtimePublisher : IRealtimePublisher
{
    private readonly IHubContext<ThreadHub> _hub;

    public SignalRRealtimePublisher(IHubContext<ThreadHub> hub) => _hub = hub;

    public async Task PublishPostEventAsync(RealtimePostEvent evt, CancellationToken ct = default)
    {
        await _hub.Clients.Group(ThreadHub.ThreadGroup(evt.ThreadId))
            .SendAsync("postEvent", evt, ct);
        await _hub.Clients.Group(ThreadHub.ForumGroup(evt.ForumId))
            .SendAsync("postEvent", evt, ct);
    }
}
