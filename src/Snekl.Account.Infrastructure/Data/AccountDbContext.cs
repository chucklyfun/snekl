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
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<OAuthApp> OAuthApps => Set<OAuthApp>();
    public DbSet<OAuthAuthorizationCode> OAuthAuthorizationCodes => Set<OAuthAuthorizationCode>();
    public DbSet<OAuthRefreshToken> OAuthRefreshTokens => Set<OAuthRefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuthServerConfig>(e => e.HasKey(x => x.Id));

        modelBuilder.Entity<PersonCluster>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CanonicalUsername).IsUnique();
            e.HasIndex(x => x.Email)
                .IsUnique()
                .HasFilter("\"EmailVerifiedAt\" IS NOT NULL");
            e.HasIndex(x => x.Did).IsUnique();
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

        modelBuilder.Entity<PasswordResetToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash);
            e.HasOne(x => x.AuthIdentity)
                .WithMany()
                .HasForeignKey(x => x.AuthIdentityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailVerificationToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash);
            e.HasOne(x => x.PersonCluster)
                .WithMany()
                .HasForeignKey(x => x.PersonClusterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OAuthApp>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ClientId).IsUnique();
            e.Property(x => x.ClientId).HasMaxLength(128);
            e.Property(x => x.Name).HasMaxLength(256);
        });

        modelBuilder.Entity<OAuthAuthorizationCode>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CodeHash).IsUnique();
            e.HasOne(x => x.App).WithMany().HasForeignKey(x => x.AppId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.PersonCluster).WithMany().HasForeignKey(x => x.PersonClusterId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Identity).WithMany().HasForeignKey(x => x.IdentityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OAuthRefreshToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.App).WithMany().HasForeignKey(x => x.AppId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.PersonCluster).WithMany().HasForeignKey(x => x.PersonClusterId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Identity).WithMany().HasForeignKey(x => x.IdentityId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task EnsureOAuthSchemaAsync(CancellationToken ct = default)
    {
        await Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "PersonClusters" ADD COLUMN IF NOT EXISTS "IsBot" boolean NOT NULL DEFAULT false;
            ALTER TABLE "PersonClusters" ADD COLUMN IF NOT EXISTS "OAuthAppId" uuid NULL;
            CREATE TABLE IF NOT EXISTS "OAuthApps" (
                "Id" uuid NOT NULL,
                "ClientId" character varying(128) NOT NULL,
                "ClientSecretHash" text NOT NULL,
                "Name" character varying(256) NOT NULL,
                "RedirectUrisJson" text NOT NULL,
                "AdminUrl" text NULL,
                "IframeUrl" text NULL,
                "CreatedByPersonClusterId" uuid NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_OAuthApps" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_OAuthApps_ClientId" ON "OAuthApps" ("ClientId");
            CREATE TABLE IF NOT EXISTS "OAuthAuthorizationCodes" (
                "Id" uuid NOT NULL,
                "CodeHash" text NOT NULL,
                "AppId" uuid NOT NULL,
                "PersonClusterId" uuid NOT NULL,
                "IdentityId" uuid NOT NULL,
                "RedirectUri" text NOT NULL,
                "CodeChallenge" text NOT NULL,
                "ContentBaseUrl" text NOT NULL,
                "InstallId" uuid NOT NULL,
                "ExpiresAt" timestamp with time zone NOT NULL,
                "UsedAt" timestamp with time zone NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_OAuthAuthorizationCodes" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_OAuthAuthorizationCodes_CodeHash" ON "OAuthAuthorizationCodes" ("CodeHash");
            CREATE TABLE IF NOT EXISTS "OAuthRefreshTokens" (
                "Id" uuid NOT NULL,
                "TokenHash" text NOT NULL,
                "AppId" uuid NOT NULL,
                "PersonClusterId" uuid NOT NULL,
                "IdentityId" uuid NOT NULL,
                "TokenUse" text NOT NULL,
                "ContentBaseUrl" text NOT NULL,
                "InstallId" uuid NOT NULL,
                "ExpiresAt" timestamp with time zone NOT NULL,
                "RevokedAt" timestamp with time zone NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_OAuthRefreshTokens" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_OAuthRefreshTokens_TokenHash" ON "OAuthRefreshTokens" ("TokenHash");
            """,
            ct);
    }
}
