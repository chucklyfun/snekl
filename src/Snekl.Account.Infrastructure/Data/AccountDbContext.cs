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
    public DbSet<AuthOperator> AuthOperators => Set<AuthOperator>();
    public DbSet<RingSession> RingSessions => Set<RingSession>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<OAuthApp> OAuthApps => Set<OAuthApp>();
    public DbSet<OAuthAuthorizationCode> OAuthAuthorizationCodes => Set<OAuthAuthorizationCode>();
    public DbSet<OAuthRefreshToken> OAuthRefreshTokens => Set<OAuthRefreshToken>();
    public DbSet<TwoFactorRecoveryCode> TwoFactorRecoveryCodes => Set<TwoFactorRecoveryCode>();
    public DbSet<TwoFactorChallenge> TwoFactorChallenges => Set<TwoFactorChallenge>();
    public DbSet<BusinessAccount> BusinessAccounts => Set<BusinessAccount>();

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

        modelBuilder.Entity<AuthOperator>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Did).IsUnique();
            e.Property(x => x.Did).HasMaxLength(512);
            e.Property(x => x.Username).HasMaxLength(128);
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

        modelBuilder.Entity<TwoFactorRecoveryCode>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.AuthIdentityId);
            e.Property(x => x.CodeHash).HasMaxLength(128);
            e.HasOne(x => x.AuthIdentity)
                .WithMany()
                .HasForeignKey(x => x.AuthIdentityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TwoFactorChallenge>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(128);
            e.HasOne(x => x.AuthIdentity)
                .WithMany()
                .HasForeignKey(x => x.AuthIdentityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BusinessAccount>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PersonClusterId);
            e.Property(x => x.BusinessName).HasMaxLength(200);
            e.Property(x => x.ContactName).HasMaxLength(200);
            e.Property(x => x.ContactEmail).HasMaxLength(254);
            e.Property(x => x.Website).HasMaxLength(300);
            e.Property(x => x.OtherServices).HasMaxLength(500);
            e.Property(x => x.Message).HasMaxLength(2000);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.ReviewedBy).HasMaxLength(512);
            e.Property(x => x.ServerDid).HasMaxLength(512);
            e.Property(x => x.ServerBaseUrl).HasMaxLength(512);
            e.HasIndex(x => x.ServerId)
                .IsUnique()
                .HasFilter("\"ServerId\" IS NOT NULL");
            e.HasOne(x => x.PersonCluster)
                .WithMany()
                .HasForeignKey(x => x.PersonClusterId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task EnsureOperatorSchemaAsync(CancellationToken ct = default)
    {
        await Database.ExecuteSqlRawAsync(
            """ALTER TABLE "SettingsReplicas" ADD COLUMN IF NOT EXISTS "FederationsJson" text NOT NULL DEFAULT '[]';""",
            ct);
        await Database.ExecuteSqlRawAsync(
            """ALTER TABLE "SettingsReplicas" ADD COLUMN IF NOT EXISTS "ForumFavoritesJson" text NOT NULL DEFAULT '[]';""",
            ct);
        await Database.ExecuteSqlRawAsync(
            """ALTER TABLE "SettingsReplicas" ADD COLUMN IF NOT EXISTS "ForumNavLayoutJson" text NOT NULL DEFAULT '[]';""",
            ct);
        await Database.ExecuteSqlRawAsync(
            """ALTER TABLE "SettingsReplicas" ADD COLUMN IF NOT EXISTS "FollowsJson" text NOT NULL DEFAULT '[]';""",
            ct);
        await Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "AuthOperators" (
                "Id" uuid NOT NULL,
                "Did" character varying(512) NOT NULL,
                "Username" character varying(128) NOT NULL,
                "PasswordHash" text NULL,
                "InvitedByOperatorId" uuid NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_AuthOperators" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AuthOperators_Did" ON "AuthOperators" ("Did");
            """,
            ct);
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
        await Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "AuthServerConfig" ADD COLUMN IF NOT EXISTS "TotpProtectionKey" text NOT NULL DEFAULT '';
            ALTER TABLE "AuthIdentities" ADD COLUMN IF NOT EXISTS "TotpSecretProtected" text NULL;
            ALTER TABLE "AuthIdentities" ADD COLUMN IF NOT EXISTS "TotpPendingSecretProtected" text NULL;
            ALTER TABLE "AuthIdentities" ADD COLUMN IF NOT EXISTS "TotpEnabledAt" timestamp with time zone NULL;
            ALTER TABLE "AuthIdentities" ADD COLUMN IF NOT EXISTS "TotpLastAcceptedStep" bigint NULL;
            CREATE TABLE IF NOT EXISTS "TwoFactorRecoveryCodes" (
                "Id" uuid NOT NULL,
                "AuthIdentityId" uuid NOT NULL,
                "CodeHash" character varying(128) NOT NULL,
                "UsedAt" timestamp with time zone NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_TwoFactorRecoveryCodes" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_TwoFactorRecoveryCodes_AuthIdentities_AuthIdentityId"
                    FOREIGN KEY ("AuthIdentityId") REFERENCES "AuthIdentities" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_TwoFactorRecoveryCodes_AuthIdentityId"
                ON "TwoFactorRecoveryCodes" ("AuthIdentityId");
            CREATE TABLE IF NOT EXISTS "TwoFactorChallenges" (
                "Id" uuid NOT NULL,
                "AuthIdentityId" uuid NOT NULL,
                "TokenHash" character varying(128) NOT NULL,
                "ExpiresAt" timestamp with time zone NOT NULL,
                "UsedAt" timestamp with time zone NULL,
                "FailedAttempts" integer NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_TwoFactorChallenges" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_TwoFactorChallenges_AuthIdentities_AuthIdentityId"
                    FOREIGN KEY ("AuthIdentityId") REFERENCES "AuthIdentities" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_TwoFactorChallenges_TokenHash"
                ON "TwoFactorChallenges" ("TokenHash");
            ALTER TABLE "TwoFactorChallenges" ADD COLUMN IF NOT EXISTS "DeviceKeyThumbprint" text NULL;
            """,
            ct);
        await Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "BusinessAccounts" (
                "Id" uuid NOT NULL,
                "PersonClusterId" uuid NOT NULL,
                "BusinessName" character varying(200) NOT NULL,
                "ContactName" character varying(200) NOT NULL,
                "ContactEmail" character varying(254) NOT NULL,
                "Website" character varying(300) NULL,
                "ServicesJson" text NOT NULL,
                "OtherServices" character varying(500) NULL,
                "Message" character varying(2000) NULL,
                "Status" character varying(32) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_BusinessAccounts" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_BusinessAccounts_PersonClusters_PersonClusterId"
                    FOREIGN KEY ("PersonClusterId") REFERENCES "PersonClusters" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_BusinessAccounts_PersonClusterId"
                ON "BusinessAccounts" ("PersonClusterId");
            ALTER TABLE "BusinessAccounts" ADD COLUMN IF NOT EXISTS "ReviewedAt" timestamp with time zone NULL;
            ALTER TABLE "BusinessAccounts" ADD COLUMN IF NOT EXISTS "ReviewedBy" character varying(512) NULL;
            ALTER TABLE "BusinessAccounts" ADD COLUMN IF NOT EXISTS "ServerId" uuid NULL;
            ALTER TABLE "BusinessAccounts" ADD COLUMN IF NOT EXISTS "ServerDid" character varying(512) NULL;
            ALTER TABLE "BusinessAccounts" ADD COLUMN IF NOT EXISTS "ServerBaseUrl" character varying(512) NULL;
            ALTER TABLE "BusinessAccounts" ADD COLUMN IF NOT EXISTS "ServerLinkedAt" timestamp with time zone NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_BusinessAccounts_ServerId"
                ON "BusinessAccounts" ("ServerId") WHERE "ServerId" IS NOT NULL;
            """,
            ct);
    }
}
