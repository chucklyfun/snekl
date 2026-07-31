using Microsoft.EntityFrameworkCore;
using Snekl.Account.Domain.Entities;

namespace Snekl.Account.Infrastructure.Data;

public class AccountDbContext : DbContext
{
    public AccountDbContext(DbContextOptions<AccountDbContext> options) : base(options) { }

    public DbSet<AuthServerConfig> AuthServerConfig => Set<AuthServerConfig>();
    public DbSet<PersonCluster> PersonClusters => Set<PersonCluster>();
    public DbSet<AuthIdentity> AuthIdentities => Set<AuthIdentity>();
    public DbSet<AuthPartnerTrust> PartnerTrusts => Set<AuthPartnerTrust>();
    public DbSet<UserSettingsReplica> SettingsReplicas => Set<UserSettingsReplica>();
    public DbSet<RingSession> RingSessions => Set<RingSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuthServerConfig>(e => e.HasKey(x => x.Id));

        modelBuilder.Entity<PersonCluster>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CanonicalUsername);
        });

        modelBuilder.Entity<AuthIdentity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.AuthServerUrl, x.PersonClusterId });
            e.HasOne(x => x.PersonCluster).WithMany(x => x.Identities).HasForeignKey(x => x.PersonClusterId);
        });

        modelBuilder.Entity<AuthPartnerTrust>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PartnerBaseUrl).IsUnique();
        });

        modelBuilder.Entity<UserSettingsReplica>(e =>
        {
            e.HasKey(x => x.PersonClusterId);
            e.HasOne(x => x.PersonCluster).WithOne(x => x.Settings).HasForeignKey<UserSettingsReplica>(x => x.PersonClusterId);
        });

        modelBuilder.Entity<RingSession>(e => e.HasKey(x => x.Id));
    }
}
