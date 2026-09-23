using Snekl.Domain.Enums;

namespace Snekl.Domain.Entities;

public class ExtensionInstall
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ClientId { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string? AdminUrl { get; set; }
    public string? IframeUrl { get; set; }
    public Guid InstalledByUserId { get; set; }
    public User? InstalledBy { get; set; }
    public string InstalledByDid { get; set; } = string.Empty;
    public string ResourceType { get; set; } = Snekl.Domain.ExtensionResourceTypes.Forum;
    public Guid? ForumId { get; set; }
    public Forum? Forum { get; set; }
    public ServerPermissions ServerPermissions { get; set; }
    public ForumPermissions ForumPermissions { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public bool IsActive => RevokedAt is null;
}
