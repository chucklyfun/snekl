namespace Snekl.Domain.Entities;

public class Emote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? MediaAssetId { get; set; }
    public MediaAsset? MediaAsset { get; set; }
    public Guid? ForumId { get; set; }
    public Forum? Forum { get; set; }
    public Guid CreatedByUserId { get; set; }
    public User? CreatedBy { get; set; }
    public bool IsAnimated { get; set; }
    public string? SkinsUnicode { get; set; }
    public int SortOrder { get; set; }
    public bool Highlighted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PostReaction> Reactions { get; set; } = new List<PostReaction>();
}
