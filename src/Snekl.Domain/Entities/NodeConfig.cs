using Snekl.Domain;

namespace Snekl.Domain.Entities;

public class NodeConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Did { get; set; } = string.Empty;
    public string VerificationKeyMultibase { get; set; } = string.Empty;
    public string VerificationPrivateKeyPkcs8 { get; set; } = string.Empty;
    public string Name { get; set; } = "Snekl Node";
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When true, NSFW media on this node requires a passed age check.</summary>
    public bool NsfwAgeCheckRequired { get; set; }

    public int NsfwMinimumAge { get; set; } = NsfwAge.DefaultMinimumAge;

    public bool EmoteDefaultsSeeded { get; set; }

    /// <summary>When set, member and federation traffic for this server is rejected.</summary>
    public DateTime? DisabledAt { get; set; }

    /// <summary>First vote total that notifies the author. Later notices are ten times the previous level.</summary>
    public int VoteAlertBase { get; set; } = Enums.VoteAlertLevels.DefaultBase;

    /// <summary>Sealed member whose server powers cannot be revoked by other users.</summary>
    public Guid? OwnerUserId { get; set; }
    public User? Owner { get; set; }

    /// <summary>Also trust the host's active auth issuers (read live). Only matters for spawned servers.</summary>
    public bool InheritHostAuthIssuers { get; set; } = true;

    /// <summary>Also use the host's accepted hubs (read live). Only matters for spawned servers.</summary>
    public bool InheritHostHubs { get; set; } = true;
}
