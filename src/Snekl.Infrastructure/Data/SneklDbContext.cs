using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Entities;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NodeConfig>(e => e.HasKey(x => x.Id));

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
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
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasOne(x => x.CreatedBy).WithMany(x => x.Forums).HasForeignKey(x => x.CreatedByUserId);
        });

        modelBuilder.Entity<Post>(e =>
        {
            e.HasKey(x => x.Id);
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
    }
}
