namespace Snekl.Domain.Identity;

/// <summary>Shared identity reference used by content servers and federation.</summary>
public class PersonClusterRecord
{
    public Guid Id { get; set; }
    public string CanonicalUsername { get; set; } = string.Empty;
    public string PrimaryAuthServerUrl { get; set; } = string.Empty;
    public string? DisambiguationTag { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AuthorIdentityMap
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PersonClusterId { get; set; }
    public PersonClusterRecord? PersonCluster { get; set; }
    public string IssuerUrl { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
    public DateTime MappedAt { get; set; } = DateTime.UtcNow;
}

public class TrustedAuthIssuer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string IssuerUrl { get; set; } = string.Empty;
    public string JwksUrl { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
