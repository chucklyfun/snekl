namespace Snekl.Account.Domain.Entities;

public class AuthServerConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Snekl Auth";
    public string BaseUrl { get; set; } = "http://localhost:5100";
    public string Did { get; set; } = string.Empty;
    public string VerificationDid { get; set; } = string.Empty;
    public string VerificationKeyMultibase { get; set; } = string.Empty;
    public string VerificationPrivateKeyPkcs8 { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PersonCluster
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Canonical W3C DID (did:key) — stable identity across auth servers and content.</summary>
    public string Did { get; set; } = string.Empty;
    public string PublicKeyMultibase { get; set; } = string.Empty;
    public string CanonicalUsername { get; set; } = string.Empty;
    public string PrimaryAuthServerUrl { get; set; } = string.Empty;
    public string? DisambiguationTag { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime? EmailVerifiedAt { get; set; }
    public string Bio { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsBot { get; set; }
    public Guid? OAuthAppId { get; set; }
    public OAuthApp? OAuthApp { get; set; }

    public ICollection<AuthIdentity> Identities { get; set; } = new List<AuthIdentity>();
    public UserSettingsReplica? Settings { get; set; }
}

public class AuthIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PersonClusterId { get; set; }
    public PersonCluster? PersonCluster { get; set; }
    public string AuthServerUrl { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AuthPartnerTrust
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PartnerBaseUrl { get; set; } = string.Empty;
    public string? PartnerSigningKey { get; set; }
    public bool LocalAcknowledged { get; set; }
    public bool RemoteAcknowledged { get; set; }
    public bool IsMutual => LocalAcknowledged && RemoteAcknowledged;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class UserSettingsReplica
{
    public Guid PersonClusterId { get; set; }
    public PersonCluster? PersonCluster { get; set; }
    public string ProfileJson { get; set; } = "{}";
    public string ContentServerMembershipsJson { get; set; } = "[]";
    public string FederationsJson { get; set; } = "[]";
    public string ForumFavoritesJson { get; set; } = "[]";
    public string ForumNavLayoutJson { get; set; } = "[]";
    public string FollowsJson { get; set; } = "[]";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class RingSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PersonClusterId { get; set; }
    public Guid PrimaryIdentityId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}

public class PasswordResetToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AuthIdentityId { get; set; }
    public AuthIdentity? AuthIdentity { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class EmailVerificationToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PersonClusterId { get; set; }
    public PersonCluster? PersonCluster { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class OAuthApp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecretHash { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RedirectUrisJson { get; set; } = "[]";
    public string? AdminUrl { get; set; }
    public string? IframeUrl { get; set; }
    public Guid? CreatedByPersonClusterId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class OAuthAuthorizationCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CodeHash { get; set; } = string.Empty;
    public Guid AppId { get; set; }
    public OAuthApp? App { get; set; }
    public Guid PersonClusterId { get; set; }
    public PersonCluster? PersonCluster { get; set; }
    public Guid IdentityId { get; set; }
    public AuthIdentity? Identity { get; set; }
    public string RedirectUri { get; set; } = string.Empty;
    public string CodeChallenge { get; set; } = string.Empty;
    public string ContentBaseUrl { get; set; } = string.Empty;
    public Guid InstallId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class OAuthRefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TokenHash { get; set; } = string.Empty;
    public Guid AppId { get; set; }
    public OAuthApp? App { get; set; }
    public Guid PersonClusterId { get; set; }
    public PersonCluster? PersonCluster { get; set; }
    public Guid IdentityId { get; set; }
    public AuthIdentity? Identity { get; set; }
    public string TokenUse { get; set; } = "install";
    public string ContentBaseUrl { get; set; } = string.Empty;
    public Guid InstallId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
