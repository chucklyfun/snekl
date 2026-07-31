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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NodeConfig>(e =>
        {
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
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
    }
}
