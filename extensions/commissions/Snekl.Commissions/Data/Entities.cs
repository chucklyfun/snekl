namespace Snekl.Commissions.Data;

public class NodeLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ContentBaseUrl { get; set; } = string.Empty;
    public Guid? InstallId { get; set; }
    public string? InstallAccessToken { get; set; }
    public string? InstallRefreshToken { get; set; }
    public DateTime? InstallExpiresAt { get; set; }
    public string? BotUsername { get; set; }
    public string? BotAccessToken { get; set; }
    public string? BotRefreshToken { get; set; }
    public DateTime? BotExpiresAt { get; set; }
    public string? LastAction { get; set; }
    public string? LastError { get; set; }
}

public class OAuthPending
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string State { get; set; } = string.Empty;
    public string Verifier { get; set; } = string.Empty;
    public string ContentBaseUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class HostCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? AccountId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsOperator { get; set; }
    public bool IsArtist { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class HostInvite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string InvitedByUsername { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class HostSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TokenHash { get; set; } = string.Empty;
    public Guid? AccountId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsOperator { get; set; }
    public bool IsArtist { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AccessGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ContentBaseUrl { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ArtistStatus { get; set; } = "none";
    public string ClientStatus { get; set; } = "none";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ApprovalPolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ContentBaseUrl { get; set; } = string.Empty;
    /// <summary>nest or server. Server is the exception; nest roles are the usual case.</summary>
    public string Scope { get; set; } = "nest";
    public Guid ForumId { get; set; }
    public string ForumName { get; set; } = string.Empty;
    public string ArtistRoles { get; set; } = string.Empty;
    public string ClientRoles { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class MemberRoleSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ContentBaseUrl { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string RoleNames { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class MemberSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TokenHash { get; set; } = string.Empty;
    public string ContentBaseUrl { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ArtistStudio
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ContentBaseUrl { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string TermsOfService { get; set; } = string.Empty;
    public bool AcceptingCommissions { get; set; }
    public string PortfolioNote { get; set; } = string.Empty;
    /// <summary>CCBill client account number (merchant). Required for adult CCBill checkout.</summary>
    public string? CcBillClientAcc { get; set; }
    /// <summary>CCBill subaccount, usually four digits.</summary>
    public string? CcBillSubacc { get; set; }
    /// <summary>FlexForms form id (UUID).</summary>
    public string? CcBillFlexFormId { get; set; }
    /// <summary>Dynamic-pricing salt. Write-only after save; never returned by the API.</summary>
    public string? CcBillSalt { get; set; }
    /// <summary>Optional CCBill affiliate id for Snekl when the artist enrolled the platform.</summary>
    public string? CcBillAffiliateId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<PortfolioItem> Portfolio { get; set; } = new List<PortfolioItem>();
    public ICollection<CommissionListing> Listings { get; set; } = new List<CommissionListing>();
}

public class PortfolioItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StudioId { get; set; }
    public ArtistStudio? Studio { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CommissionListing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StudioId { get; set; }
    public ArtistStudio? Studio { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PricingMode { get; set; } = string.Empty;
    public int? PriceCents { get; set; }
    public string Currency { get; set; } = "USD";
    public int OpenSlots { get; set; }
    public string? Terms { get; set; }
    public bool IsOpen { get; set; } = true;
    /// <summary>Adult/NSFW listing. Must not use Stripe escrow; CCBill or off-platform only.</summary>
    public bool Adult { get; set; }
    public Guid? SharePostId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<CommissionOrder> Orders { get; set; } = new List<CommissionOrder>();
}

public class CommissionOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public CommissionListing? Listing { get; set; }
    public string ContentBaseUrl { get; set; } = string.Empty;
    public Guid ArtistUserId { get; set; }
    public string ArtistUsername { get; set; } = string.Empty;
    public Guid ClientUserId { get; set; }
    public string ClientUsername { get; set; } = string.Empty;
    public string Brief { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PricingMode { get; set; } = string.Empty;
    public int? PriceCents { get; set; }
    public string Currency { get; set; } = "USD";
    public string PaymentMethod { get; set; } = string.Empty;
    public string EscrowState { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<CommissionTimelineEntry> Timeline { get; set; } = new List<CommissionTimelineEntry>();
    public ICollection<CommissionMilestone> Milestones { get; set; } = new List<CommissionMilestone>();
}

public class CommissionMilestone
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public CommissionOrder? Order { get; set; }
    public int Sequence { get; set; }
    public string Label { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    /// <summary>pending, paid, refunded, charged_back</summary>
    public string Status { get; set; } = MilestoneStatuses.Pending;
    public string? CcBillTransactionId { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class MilestoneStatuses
{
    public const string Pending = "pending";
    public const string Paid = "paid";
    public const string Refunded = "refunded";
    public const string ChargedBack = "charged_back";
}

public class CommissionTimelineEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public CommissionOrder? Order { get; set; }
    public Guid ActorUserId { get; set; }
    public string ActorUsername { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CcBillWebhookEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TransactionId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public Guid? MilestoneId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
