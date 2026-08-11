using Microsoft.AspNetCore.SignalR;

namespace Snekl.Api.Realtime;

public class ThreadHub : Hub
{
    public static string ThreadGroup(Guid threadId) => $"thread:{threadId:D}";
    public static string ForumGroup(Guid forumId) => $"forum:{forumId:D}";

    public Task JoinThread(string threadId)
    {
        if (!Guid.TryParse(threadId, out var id))
            throw new HubException("Invalid thread id.");
        return Groups.AddToGroupAsync(Context.ConnectionId, ThreadGroup(id));
    }

    public Task LeaveThread(string threadId)
    {
        if (!Guid.TryParse(threadId, out var id))
            throw new HubException("Invalid thread id.");
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, ThreadGroup(id));
    }

    public Task JoinForum(string forumId)
    {
        if (!Guid.TryParse(forumId, out var id))
            throw new HubException("Invalid forum id.");
        return Groups.AddToGroupAsync(Context.ConnectionId, ForumGroup(id));
    }

    public Task LeaveForum(string forumId)
    {
        if (!Guid.TryParse(forumId, out var id))
            throw new HubException("Invalid forum id.");
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, ForumGroup(id));
    }
}
