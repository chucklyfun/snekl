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
    public DbSet<PostUpvote> PostUpvotes => Set<PostUpvote>();
    public DbSet<Emote> Emotes => Set<Emote>();
    public DbSet<PostReaction> PostReactions => Set<PostReaction>();
    public DbSet<PostRevision> PostRevisions => Set<PostRevision>();
    public DbSet<ChangeLogEntry> ChangeLogEntries => Set<ChangeLogEntry>();
    public DbSet<PeerNode> PeerNodes => Set<PeerNode>();
    public DbSet<UserStatus> UserStatuses => Set<UserStatus>();
    public DbSet<CuratedList> CuratedLists => Set<CuratedList>();
    public DbSet<ListEntry> ListEntries => Set<ListEntry>();
    public DbSet<ListSubscription> ListSubscriptions => Set<ListSubscription>();
    public DbSet<ListEntryIgnore> ListEntryIgnores => Set<ListEntryIgnore>();
    public DbSet<PersonClusterRecord> PersonClusterRecords => Set<PersonClusterRecord>();
    public DbSet<AuthorIdentityMap> AuthorIdentityMaps => Set<AuthorIdentityMap>();
    public DbSet<TrustedAuthIssuer> TrustedAuthIssuers => Set<TrustedAuthIssuer>();
    public DbSet<SiteAdmin> SiteAdmins => Set<SiteAdmin>();
    public DbSet<SuperUserGrant> SuperUserGrants => Set<SuperUserGrant>();
    public DbSet<HubInvite> HubInvites => Set<HubInvite>();
    public DbSet<ServerRole> ServerRoles => Set<ServerRole>();
    public DbSet<ServerRoleAssignment> ServerRoleAssignments => Set<ServerRoleAssignment>();
    public DbSet<ForumRole> ForumRoles => Set<ForumRole>();
    public DbSet<ForumRoleAssignment> ForumRoleAssignments => Set<ForumRoleAssignment>();
    public DbSet<ForumMembership> ForumMemberships => Set<ForumMembership>();
    public DbSet<ForumFlairTemplate> ForumFlairTemplates => Set<ForumFlairTemplate>();
    public DbSet<ForumMemberFlair> ForumMemberFlairs => Set<ForumMemberFlair>();
    public DbSet<ForumMemberAlias> ForumMemberAliases => Set<ForumMemberAlias>();
    public DbSet<ForumMemberBan> ForumMemberBans => Set<ForumMemberBan>();
    public DbSet<PostReport> PostReports => Set<PostReport>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<PostMedia> PostMedia => Set<PostMedia>();
    public DbSet<ExtensionInstall> ExtensionInstalls => Set<ExtensionInstall>();
    public DbSet<HubOutboxMessage> HubOutbox => Set<HubOutboxMessage>();
    public DbSet<ContentBlockType> ContentBlockTypes => Set<ContentBlockType>();
    public DbSet<ContentBlockPermission> ContentBlockPermissions => Set<ContentBlockPermission>();
    public DbSet<Watercooler> Watercoolers => Set<Watercooler>();
    public DbSet<WatercoolerRole> WatercoolerRoles => Set<WatercoolerRole>();
    public DbSet<WatercoolerRoleAssignment> WatercoolerRoleAssignments => Set<WatercoolerRoleAssignment>();
    public DbSet<ForumContentBlock> ForumContentBlocks => Set<ForumContentBlock>();
    public DbSet<DirectConversationRequest> DirectConversationRequests => Set<DirectConversationRequest>();
    public DbSet<DirectConversation> DirectConversations => Set<DirectConversation>();
    public DbSet<DirectMessage> DirectMessages => Set<DirectMessage>();
    public DbSet<DirectMessageForumPreference> DirectMessageForumPreferences => Set<DirectMessageForumPreference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NodeConfig>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.SetNull);
        });

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
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ParentForum).WithMany(x => x.Subforums).HasForeignKey(x => x.ParentForumId);
            e.HasOne(x => x.AvatarMedia).WithMany().HasForeignKey(x => x.AvatarMediaAssetId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.BannerMedia).WithMany().HasForeignKey(x => x.BannerMediaAssetId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.ApprovalStatus).HasMaxLength(32).HasDefaultValue("approved");
            e.Property(x => x.NsfwAgeCheckExplanation).HasMaxLength(2000);
        });

        modelBuilder.Entity<Post>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ParentPostId);
            e.HasIndex(x => new { x.ForumId, x.ParentPostId });
            e.HasIndex(x => new { x.WatercoolerId, x.ParentPostId });
            e.HasIndex(x => x.QuotedPostId);
            e.HasOne(x => x.Forum).WithMany(x => x.Posts).HasForeignKey(x => x.ForumId);
            e.HasOne(x => x.Watercooler).WithMany(x => x.Posts).HasForeignKey(x => x.WatercoolerId);
            e.HasOne(x => x.Author).WithMany(x => x.Posts).HasForeignKey(x => x.AuthorUserId);
            e.HasOne(x => x.ParentPost).WithMany(x => x.Replies).HasForeignKey(x => x.ParentPostId);
            e.HasOne(x => x.QuotedPost).WithMany().HasForeignKey(x => x.QuotedPostId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.ReplyPolicy).HasMaxLength(32);
        });

        modelBuilder.Entity<PostUpvote>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PostId, x.UserId }).IsUnique();
            e.HasOne(x => x.Post).WithMany(x => x.Upvotes).HasForeignKey(x => x.PostId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        modelBuilder.Entity<PostRevision>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PostId, x.CreatedAt });
            e.HasOne(x => x.Post).WithMany(x => x.Revisions).HasForeignKey(x => x.PostId);
            e.HasOne(x => x.EditedBy).WithMany().HasForeignKey(x => x.EditedByUserId);
        });

        modelBuilder.Entity<ChangeLogEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Scope, x.CreatedAt });
            e.HasIndex(x => new { x.ForumId, x.CreatedAt });
            e.HasIndex(x => new { x.WatercoolerId, x.CreatedAt });
            e.Property(x => x.Scope).HasMaxLength(32);
            e.Property(x => x.EntityType).HasMaxLength(64);
            e.Property(x => x.Action).HasMaxLength(32);
            e.Property(x => x.Summary).HasMaxLength(512);
            e.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorUserId)
                .OnDelete(DeleteBehavior.SetNull);
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

        modelBuilder.Entity<ListEntryIgnore>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.ListId, x.TargetUserId }).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.List).WithMany().HasForeignKey(x => x.ListId);
            e.HasOne(x => x.TargetUser).WithMany().HasForeignKey(x => x.TargetUserId);
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
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasIndex(x => x.Did).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        modelBuilder.Entity<SuperUserGrant>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.GrantedBySiteAdmin).WithMany().HasForeignKey(x => x.GrantedBySiteAdminId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<HubInvite>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Status);
            e.Property(x => x.HubBaseUrl).HasMaxLength(2048);
            e.Property(x => x.HubName).HasMaxLength(256);
            e.Property(x => x.RequestedByDid).HasMaxLength(512);
            e.Property(x => x.RequestedByUsername).HasMaxLength(128);
            e.Property(x => x.Status).HasMaxLength(32);
        });

        modelBuilder.Entity<ServerRole>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IsEveryone).HasFilter("\"IsEveryone\" = true");
            e.Property(x => x.Modes)
                .HasColumnType("jsonb")
                .HasConversion(PermissionModesConverter.Converter, PermissionModesConverter.Comparer)
                .HasDefaultValueSql("'{}'::jsonb");
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
            e.Property(x => x.Modes)
                .HasColumnType("jsonb")
                .HasConversion(PermissionModesConverter.Converter, PermissionModesConverter.Comparer)
                .HasDefaultValueSql("'{}'::jsonb");
        });

        modelBuilder.Entity<ForumRoleAssignment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ForumId, x.UserId, x.RoleId }).IsUnique();
            e.HasOne(x => x.Forum).WithMany().HasForeignKey(x => x.ForumId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Role).WithMany(x => x.Assignments).HasForeignKey(x => x.RoleId);
        });

        modelBuilder.Entity<ForumMembership>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ForumId, x.UserId }).IsUnique();
            e.HasOne(x => x.Forum).WithMany().HasForeignKey(x => x.ForumId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        modelBuilder.Entity<HubOutboxMessage>(e =>
        {
            e.ToTable("HubOutbox");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.DeliveredAt, x.NextAttemptAt });
            e.Property(x => x.HubBaseUrl).HasMaxLength(2048);
            e.Property(x => x.Path).HasMaxLength(256);
            e.Property(x => x.EnvelopeType).HasMaxLength(64);
            e.Property(x => x.LastError).HasMaxLength(2048);
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

        modelBuilder.Entity<ForumMemberBan>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ForumId, x.UserId }).IsUnique();
            e.HasOne(x => x.Forum).WithMany().HasForeignKey(x => x.ForumId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.BannedBy).WithMany().HasForeignKey(x => x.BannedByUserId);
        });

        modelBuilder.Entity<PostReport>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ForumId, x.Status });
            e.HasIndex(x => new { x.PostId, x.ReporterUserId, x.Status });
            e.HasOne(x => x.Post).WithMany().HasForeignKey(x => x.PostId);
            e.HasOne(x => x.Forum).WithMany().HasForeignKey(x => x.ForumId);
            e.HasOne(x => x.Reporter).WithMany().HasForeignKey(x => x.ReporterUserId);
            e.HasOne(x => x.ResolvedBy).WithMany().HasForeignKey(x => x.ResolvedByUserId);
        });

        modelBuilder.Entity<MediaAsset>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UploaderUserId);
            e.HasIndex(x => x.StorageKey);
            e.HasOne(x => x.Uploader).WithMany().HasForeignKey(x => x.UploaderUserId);
        });

        modelBuilder.Entity<PostMedia>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PostId, x.SortOrder });
            e.HasIndex(x => x.MediaAssetId).IsUnique();
            e.Property(x => x.CoverText).HasMaxLength(140);
            e.HasOne(x => x.Post).WithMany(x => x.Media).HasForeignKey(x => x.PostId);
            e.HasOne(x => x.MediaAsset).WithMany(x => x.PostLinks).HasForeignKey(x => x.MediaAssetId);
        });

        modelBuilder.Entity<Emote>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(64);
            e.Property(x => x.SkinsUnicode).HasMaxLength(16);
            e.HasIndex(x => x.MediaAssetId).IsUnique().HasFilter("\"MediaAssetId\" IS NOT NULL");
            e.HasIndex(x => new { x.ForumId, x.Code }).IsUnique();
            e.HasOne(x => x.MediaAsset).WithMany(x => x.Emotes).HasForeignKey(x => x.MediaAssetId).IsRequired(false);
            e.HasOne(x => x.Forum).WithMany().HasForeignKey(x => x.ForumId);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId);
        });

        modelBuilder.Entity<PostReaction>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Emoji).HasMaxLength(16);
            e.HasIndex(x => x.PostId);
            e.HasOne(x => x.Post).WithMany(x => x.Reactions).HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Emote).WithMany(x => x.Reactions).HasForeignKey(x => x.EmoteId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExtensionInstall>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => x.InstalledByUserId);
            e.Property(x => x.ClientId).HasMaxLength(128);
            e.Property(x => x.AppName).HasMaxLength(256);
            e.Property(x => x.ResourceType).HasMaxLength(32);
            e.Property(x => x.ServerPermissions).HasConversion<long>();
            e.Property(x => x.ForumPermissions).HasConversion<long>();
            e.HasOne(x => x.InstalledBy).WithMany().HasForeignKey(x => x.InstalledByUserId);
            e.HasOne(x => x.Forum).WithMany().HasForeignKey(x => x.ForumId);
        });

        modelBuilder.Entity<ContentBlockType>(e =>
        {
            e.ToTable("ContentBlockTypes");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(128);
        });

        modelBuilder.Entity<ContentBlockPermission>(e =>
        {
            e.ToTable("ContentBlockPermissions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BlockTypeId, x.Name, x.Scope }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Label).HasMaxLength(256);
            e.Property(x => x.Scope).HasMaxLength(32);
            e.HasOne(x => x.BlockType).WithMany(t => t.Permissions).HasForeignKey(x => x.BlockTypeId);
        });

        modelBuilder.Entity<Watercooler>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ParentForumId, x.Slug }).IsUnique();
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId);
            e.HasOne(x => x.ParentForum).WithMany(x => x.Watercoolers).HasForeignKey(x => x.ParentForumId);
            e.Property(x => x.ApprovalStatus).HasMaxLength(32).HasDefaultValue("approved");
        });

        modelBuilder.Entity<WatercoolerRole>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.WatercoolerId, x.IsEveryone }).HasFilter("\"IsEveryone\" = true");
            e.HasOne(x => x.Watercooler).WithMany(x => x.Roles).HasForeignKey(x => x.WatercoolerId);
            e.Property(x => x.Modes)
                .HasColumnType("jsonb")
                .HasConversion(PermissionModesConverter.Converter, PermissionModesConverter.Comparer)
                .HasDefaultValueSql("'{}'::jsonb");
        });

        modelBuilder.Entity<WatercoolerRoleAssignment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.WatercoolerId, x.UserId, x.RoleId }).IsUnique();
            e.HasOne(x => x.Watercooler).WithMany().HasForeignKey(x => x.WatercoolerId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Role).WithMany(x => x.Assignments).HasForeignKey(x => x.RoleId);
        });

        modelBuilder.Entity<ForumContentBlock>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ForumId, x.Slot, x.Position });
            e.Property(x => x.TypeKey).HasMaxLength(64);
            e.Property(x => x.Slot).HasMaxLength(32);
            e.HasOne(x => x.Forum).WithMany(x => x.ContentBlocks).HasForeignKey(x => x.ForumId);
            e.HasOne(x => x.Watercooler).WithMany().HasForeignKey(x => x.WatercoolerId);
        });

        modelBuilder.Entity<DirectConversationRequest>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TargetUserId, x.Status, x.CreatedAt });
            e.HasIndex(x => new { x.RequesterUserId, x.TargetUserId });
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.Message).HasMaxLength(2000);
            e.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Target).WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Forum).WithMany().HasForeignKey(x => x.ForumId);
            e.HasOne(x => x.Conversation).WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DirectConversation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserLowId, x.UserHighId }).IsUnique();
            e.HasOne(x => x.UserLow).WithMany().HasForeignKey(x => x.UserLowId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.UserHigh).WithMany().HasForeignKey(x => x.UserHighId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.OriginForum).WithMany().HasForeignKey(x => x.OriginForumId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DirectMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.Property(x => x.Body).HasMaxLength(8000);
            e.HasOne(x => x.Conversation).WithMany(x => x.Messages).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DirectMessageForumPreference>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.ForumId }).IsUnique();
            e.Property(x => x.Mode).HasMaxLength(16);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Forum).WithMany().HasForeignKey(x => x.ForumId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
