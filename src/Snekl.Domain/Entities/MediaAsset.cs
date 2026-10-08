namespace Snekl.Domain.Entities;

public class MediaAsset : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UploaderUserId { get; set; }
    public User? Uploader { get; set; }
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>Absolute URL for federated media hosted on an origin node (no local object bytes).</summary>
    public string? ExternalUrl { get; set; }

    public string ContentType { get; set; } = string.Empty;
    public long ByteSize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PostMedia> PostLinks { get; set; } = new List<PostMedia>();

    public ICollection<Emote> Emotes { get; set; } = new List<Emote>();
}
