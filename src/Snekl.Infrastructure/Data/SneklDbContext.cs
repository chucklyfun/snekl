using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Entities;
using Snekl.Domain.Identity;

namespace Snekl.Infrastructure.Data;

public class SneklDbContext : DbContext
{
    public SneklDbContext(DbContextOptions<SneklDbContext> options) : base(options) { }

    public DbSet<NodeConfig> NodeConfig => Set<NodeConfig>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Forum> Forums => Set<Forum>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PeerNode> PeerNodes => Set<PeerNode>();
    public DbSet<UserStatus> UserStatuses => Set<UserStatus>();
    public DbSet<CuratedList> CuratedLists => Set<CuratedList>();
    public DbSet<ListEntry> ListEntries => Set<ListEntry>();
    public DbSet<ListSubscription> ListSubscriptions => Set<ListSubscription>();
    public DbSet<PersonClusterRecord> PersonClusterRecords => Set<PersonClusterRecord>();
    public DbSet<AuthorIdentityMap> AuthorIdentityMaps => Set<AuthorIdentityMap>();
    public DbSet<TrustedAuthIssuer> TrustedAuthIssuers => Set<TrustedAuthIssuer>();
    public DbSet<SiteAdmin> SiteAdmins => Set<SiteAdmin>();
    public DbSet<SuperUserGrant> SuperUserGrants => Set<SuperUserGrant>();
    public DbSet<ServerRole> ServerRoles => Set<ServerRole>();
    public DbSet<ServerRoleAssignment> ServerRoleAssignments => Set<ServerRoleAssignment>();
    public DbSet<ForumRole> ForumRoles => Set<ForumRole>();
    public DbSet<ForumRoleAssignment> ForumRoleAssignments => Set<ForumRoleAssignment>();
    public DbSet<ForumFlairTemplate> ForumFlairTemplates => Set<ForumFlairTemplate>();
    public DbSet<ForumMemberFlair> ForumMemberFlairs => Set<ForumMemberFlair>();
    public DbSet<ForumMemberAlias> ForumMemberAliases => Set<ForumMemberAlias>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NodeConfig>(e => e.HasKey(x => x.Id));

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.Did).IsUnique();
            e.HasIndex(x => new { x.AuthIssuerUrl, x.AuthSubjectId }).IsUnique();
            e.HasOne(x => x.ActiveStatus).WithOne(x => x.User).HasForeignKey<UserStatus>(x => x.UserId);
        });

        modelBuilder.Entity<UserStatus>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasIndex(x => x.ExpiresAt);
        });

        modelBuilder.Entity<Forum>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ParentForumId, x.Slug }).IsUnique();
            e.HasOne(x => x.CreatedBy).WithMany(x => x.Forums).HasForeignKey(x => x.CreatedByUserId);
            e.HasOne(x => x.ParentForum).WithMany(x => x.Subforums).HasForeignKey(x => x.ParentForumId);
        });

        modelBuilder.Entity<Post>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ParentPostId);
            e.HasIndex(x => new { x.ForumId, x.ParentPostId });
            e.HasOne(x => x.Forum).WithMany(x => x.Posts).HasForeignKey(x => x.ForumId);
            e.HasOne(x => x.Author).WithMany(x => x.Posts).HasForeignKey(x => x.AuthorUserId);
            e.HasOne(x => x.ParentPost).WithMany(x => x.Replies).HasForeignKey(x => x.ParentPostId);
        });

        modelBuilder.Entity<PeerNode>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.BaseUrl).IsUnique();
        });

        modelBuilder.Entity<CuratedList>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Owner).WithMany(x => x.OwnedLists).HasForeignKey(x => x.OwnerUserId);
        });

        modelBuilder.Entity<ListEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ListId, x.TargetUserId }).IsUnique();
            e.HasOne(x => x.List).WithMany(x => x.Entries).HasForeignKey(x => x.ListId);
            e.HasOne(x => x.TargetUser).WithMany().HasForeignKey(x => x.TargetUserId);
        });

        modelBuilder.Entity<ListSubscription>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.ListId }).IsUnique();
            e.HasOne(x => x.User).WithMany(x => x.ListSubscriptions).HasForeignKey(x => x.UserId);
            e.HasOne(x => x.List).WithMany(x => x.Subscriptions).HasForeignKey(x => x.ListId);
        });

        modelBuilder.Entity<PersonClusterRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CanonicalUsername);
            e.HasIndex(x => x.Did).IsUnique();
        });

        modelBuilder.Entity<AuthorIdentityMap>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.IssuerUrl, x.SubjectId }).IsUnique();
            e.HasIndex(x => x.AuthorDid);
            e.HasOne(x => x.PersonCluster).WithMany().HasForeignKey(x => x.PersonClusterId);
        });

        modelBuilder.Entity<TrustedAuthIssuer>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IssuerUrl).IsUnique();
        });

        modelBuilder.Entity<SiteAdmin>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<SuperUserGrant>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.GrantedBySiteAdmin).WithMany().HasForeignKey(x => x.GrantedBySiteAdminId);
        });

        modelBuilder.Entity<ServerRole>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IsEveryone).HasFilter("\"IsEveryone\" = true");
        });

        modelBuilder.Entity<ServerRoleAssignment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Role).WithMany(x => x.Assignments).HasForeignKey(x => x.RoleId);
        });

        modelBuilder.Entity<ForumRole>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ForumId, x.IsEveryone }).HasFilter("\"IsEveryone\" = true");
            e.HasOne(x => x.Forum).WithMany(x => x.Roles).HasForeignKey(x => x.ForumId);
            e.HasOne(x => x.InheritFromRole).WithMany().HasForeignKey(x => x.InheritFromRoleId);
        });

        modelBuilder.Entity<ForumRoleAssignment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ForumId, x.UserId, x.RoleId }).IsUnique();
            e.HasOne(x => x.Forum).WithMany().HasForeignKey(x => x.ForumId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Role).WithMany(x => x.Assignments).HasForeignKey(x => x.RoleId);
        });

        modelBuilder.Entity<ForumFlairTemplate>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Forum).WithMany(x => x.FlairTemplates).HasForeignKey(x => x.ForumId);
            e.HasOne(x => x.InheritFromTemplate).WithMany().HasForeignKey(x => x.InheritFromTemplateId);
        });

        modelBuilder.Entity<ForumMemberFlair>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ForumId, x.UserId }).IsUnique();
            e.HasOne(x => x.Forum).WithMany().HasForeignKey(x => x.ForumId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId);
        });

        modelBuilder.Entity<ForumMemberAlias>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ForumId, x.UserId }).IsUnique();
            e.HasOne(x => x.Forum).WithMany().HasForeignKey(x => x.ForumId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });
    }
}
