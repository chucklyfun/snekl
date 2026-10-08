namespace Snekl.Infrastructure.Data;

public interface ICurrentServer
{
    Guid? ServerId { get; }

    void Use(Guid serverId);
}

public sealed class CurrentServer : ICurrentServer
{
    public Guid? ServerId { get; private set; }

    public void Use(Guid serverId) => ServerId = serverId;
}
