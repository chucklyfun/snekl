namespace Snekl.Domain.Entities;

public class User : ExternalEntity
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public string? Did { get; set; }
    public string? AuthIssuerUrl { get; set; }
    public Guid? AuthSubjectId { get; set; }

    public string? FlairEmoji { get; set; }
    public string? FlairIconUrl { get; set; }
    public string? FlairLabel { get; set; }
    public string? NameColor { get; set; }
    public string? NameStyle { get; set; }

    public bool IsBot { get; set; }
    public string? OAuthClientId { get; set; }
    public Guid? ExtensionInstallId { get; set; }

    public DateTime? NsfwAgeAttestedAt { get; set; }
    public int? NsfwAgeAttestedMinimum { get; set; }

    public ICollection<Post> Posts { get; set; } = new List<Post>();
    public ICollection<Forum> Forums { get; set; } = new List<Forum>();
    public UserStatus? ActiveStatus { get; set; }
    public ICollection<CuratedList> OwnedLists { get; set; } = new List<CuratedList>();
    public ICollection<ListSubscription> ListSubscriptions { get; set; } = new List<ListSubscription>();
}
