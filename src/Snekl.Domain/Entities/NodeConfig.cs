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

    /// <summary>Sealed member whose server powers cannot be revoked by other users.</summary>
    public Guid? OwnerUserId { get; set; }
    public User? Owner { get; set; }
}
