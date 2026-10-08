using Snekl.Domain.Enums;

namespace Snekl.Domain.Entities;

public class Forum : ExternalEntity, IServerScoped
{
    public Guid ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public User? CreatedBy { get; set; }

    /// <summary>Protected forum owner; peer forum admins cannot demote this user.</summary>
    public Guid? OwnerUserId { get; set; }
    public User? Owner { get; set; }
    public Guid? ParentForumId { get; set; }
    public Forum? ParentForum { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Prepared nest. Visible only to server super users and the server owner until unlocked.</summary>
    public bool Locked { get; set; }

    /// <summary>Preliminary delete. Hidden from members until restored or purged.</summary>
    public bool Hidden { get; set; }
    public bool AllowAliases { get; set; } = true;
    public string ApprovalStatus { get; set; } = ForumApprovalStatuses.Approved;
    public bool NsfwAgeCheckRequired { get; set; }
    public string NsfwAgeCheckExplanation { get; set; } = string.Empty;
    public bool EmoteDefaultsSeeded { get; set; }
    public Guid? AvatarMediaAssetId { get; set; }
    public MediaAsset? AvatarMedia { get; set; }
    public Guid? BannerMediaAssetId { get; set; }
    public MediaAsset? BannerMedia { get; set; }

    public ICollection<Post> Posts { get; set; } = new List<Post>();
    public ICollection<Forum> Subforums { get; set; } = new List<Forum>();
    public ICollection<ForumRole> Roles { get; set; } = new List<ForumRole>();
    public ICollection<ForumFlairTemplate> FlairTemplates { get; set; } = new List<ForumFlairTemplate>();
    public ICollection<Watercooler> Watercoolers { get; set; } = new List<Watercooler>();
    public ICollection<ForumContentBlock> ContentBlocks { get; set; } = new List<ForumContentBlock>();
    public ICollection<ForumPage> Pages { get; set; } = new List<ForumPage>();
}
