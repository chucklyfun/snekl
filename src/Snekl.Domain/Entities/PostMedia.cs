namespace Snekl.Domain.Entities;

public class PostMedia : IServerScoped
{
    public Guid ServerId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public Post? Post { get; set; }
    public Guid MediaAssetId { get; set; }
    public MediaAsset? MediaAsset { get; set; }
    public int SortOrder { get; set; }
    public bool IsNsfw { get; set; }
    public string? CoverText { get; set; }
    public string? CensorStorageKey { get; set; }
    public string? CensorContentType { get; set; }
    public long? CensorByteSize { get; set; }
}
