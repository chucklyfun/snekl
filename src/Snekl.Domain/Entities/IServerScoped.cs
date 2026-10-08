namespace Snekl.Domain.Entities;

/// <summary>Row that belongs to one virtual content server (<see cref="NodeConfig.Id"/>).</summary>
public interface IServerScoped
{
    Guid ServerId { get; set; }
}
