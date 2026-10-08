using Microsoft.EntityFrameworkCore;

namespace Snekl.Commissions.Data;

public class CommissionsDb : DbContext
{
    public CommissionsDb(DbContextOptions<CommissionsDb> options) : base(options) { }

    public DbSet<NodeLink> NodeLinks => Set<NodeLink>();
    public DbSet<OAuthPending> OAuthPendings => Set<OAuthPending>();
    public DbSet<HostCredential> HostCredentials => Set<HostCredential>();
    public DbSet<HostInvite> HostInvites => Set<HostInvite>();
    public DbSet<HostSession> HostSessions => Set<HostSession>();
    public DbSet<AccessGrant> AccessGrants => Set<AccessGrant>();
    public DbSet<ApprovalPolicy> ApprovalPolicies => Set<ApprovalPolicy>();
    public DbSet<MemberRoleSnapshot> MemberRoleSnapshots => Set<MemberRoleSnapshot>();
    public DbSet<MemberSession> MemberSessions => Set<MemberSession>();
    public DbSet<ArtistStudio> Studios => Set<ArtistStudio>();
    public DbSet<PortfolioItem> PortfolioItems => Set<PortfolioItem>();
    public DbSet<CommissionListing> Listings => Set<CommissionListing>();
    public DbSet<CommissionOrder> Orders => Set<CommissionOrder>();
    public DbSet<CommissionMilestone> Milestones => Set<CommissionMilestone>();
    public DbSet<CommissionTimelineEntry> Timeline => Set<CommissionTimelineEntry>();
    public DbSet<CcBillWebhookEvent> CcBillWebhookEvents => Set<CcBillWebhookEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NodeLink>(e =>
        {
            e.HasIndex(x => x.ContentBaseUrl).IsUnique();
            e.Property(x => x.ContentBaseUrl).HasMaxLength(512);
        });

        modelBuilder.Entity<OAuthPending>(e =>
        {
            e.HasIndex(x => x.State).IsUnique();
            e.Property(x => x.State).HasMaxLength(128);
            e.Property(x => x.Verifier).HasMaxLength(128);
            e.Property(x => x.ContentBaseUrl).HasMaxLength(512);
        });

        modelBuilder.Entity<HostCredential>(e =>
        {
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(128);
            e.Property(x => x.PasswordHash).HasMaxLength(256);
        });

        modelBuilder.Entity<HostInvite>(e =>
        {
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(64);
            e.Property(x => x.InvitedByUsername).HasMaxLength(64);
        });

        modelBuilder.Entity<HostSession>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(128);
            e.Property(x => x.Username).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(128);
        });

        modelBuilder.Entity<AccessGrant>(e =>
        {
            e.HasIndex(x => new { x.ContentBaseUrl, x.UserId }).IsUnique();
            e.Property(x => x.ContentBaseUrl).HasMaxLength(512);
            e.Property(x => x.Username).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(128);
            e.Property(x => x.ArtistStatus).HasMaxLength(32);
            e.Property(x => x.ClientStatus).HasMaxLength(32);
        });

        modelBuilder.Entity<ApprovalPolicy>(e =>
        {
            e.Property(x => x.ContentBaseUrl).HasMaxLength(512);
            e.Property(x => x.Scope).HasMaxLength(32);
            e.Property(x => x.ForumName).HasMaxLength(128);
        });

        modelBuilder.Entity<MemberRoleSnapshot>(e =>
        {
            e.HasIndex(x => new { x.ContentBaseUrl, x.UserId }).IsUnique();
            e.Property(x => x.ContentBaseUrl).HasMaxLength(512);
        });

        modelBuilder.Entity<MemberSession>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(128);
            e.Property(x => x.ContentBaseUrl).HasMaxLength(512);
            e.Property(x => x.Username).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(128);
        });

        modelBuilder.Entity<ArtistStudio>(e =>
        {
            e.HasIndex(x => new { x.ContentBaseUrl, x.UserId }).IsUnique();
            e.Property(x => x.ContentBaseUrl).HasMaxLength(512);
            e.Property(x => x.Username).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(128);
            e.Property(x => x.CcBillClientAcc).HasMaxLength(32);
            e.Property(x => x.CcBillSubacc).HasMaxLength(16);
            e.Property(x => x.CcBillFlexFormId).HasMaxLength(64);
            e.Property(x => x.CcBillSalt).HasMaxLength(128);
            e.Property(x => x.CcBillAffiliateId).HasMaxLength(64);
        });

        modelBuilder.Entity<PortfolioItem>(e =>
        {
            e.HasOne(x => x.Studio).WithMany(x => x.Portfolio).HasForeignKey(x => x.StudioId);
            e.Property(x => x.Title).HasMaxLength(200);
        });

        modelBuilder.Entity<CommissionListing>(e =>
        {
            e.HasOne(x => x.Studio).WithMany(x => x.Listings).HasForeignKey(x => x.StudioId);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.PricingMode).HasMaxLength(32);
            e.Property(x => x.Currency).HasMaxLength(8);
        });

        modelBuilder.Entity<CommissionOrder>(e =>
        {
            e.HasOne(x => x.Listing).WithMany(x => x.Orders).HasForeignKey(x => x.ListingId);
            e.Property(x => x.ContentBaseUrl).HasMaxLength(512);
            e.Property(x => x.ArtistUsername).HasMaxLength(64);
            e.Property(x => x.ClientUsername).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.PricingMode).HasMaxLength(32);
            e.Property(x => x.Currency).HasMaxLength(8);
            e.Property(x => x.PaymentMethod).HasMaxLength(32);
            e.Property(x => x.EscrowState).HasMaxLength(32);
        });

        modelBuilder.Entity<CommissionMilestone>(e =>
        {
            e.HasOne(x => x.Order).WithMany(x => x.Milestones).HasForeignKey(x => x.OrderId);
            e.HasIndex(x => new { x.OrderId, x.Sequence }).IsUnique();
            e.HasIndex(x => x.CcBillTransactionId);
            e.Property(x => x.Label).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.CcBillTransactionId).HasMaxLength(64);
        });

        modelBuilder.Entity<CommissionTimelineEntry>(e =>
        {
            e.HasOne(x => x.Order).WithMany(x => x.Timeline).HasForeignKey(x => x.OrderId);
            e.Property(x => x.ActorUsername).HasMaxLength(64);
            e.Property(x => x.Kind).HasMaxLength(32);
        });

        modelBuilder.Entity<CcBillWebhookEvent>(e =>
        {
            e.HasIndex(x => x.TransactionId);
            e.Property(x => x.TransactionId).HasMaxLength(64);
            e.Property(x => x.EventType).HasMaxLength(64);
            e.Property(x => x.Outcome).HasMaxLength(32);
        });
    }
}
