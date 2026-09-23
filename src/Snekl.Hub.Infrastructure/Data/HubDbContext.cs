using Microsoft.EntityFrameworkCore;
using Snekl.Hub.Domain.Entities;

namespace Snekl.Hub.Infrastructure.Data;

public class HubDbContext : DbContext
{
    public HubDbContext(DbContextOptions<HubDbContext> options) : base(options) { }

    public DbSet<FederationState> FederationState => Set<FederationState>();
    public DbSet<RegisteredServer> RegisteredServers => Set<RegisteredServer>();
    public DbSet<HubOperator> HubOperators => Set<HubOperator>();
    public DbSet<FeedCard> FeedCards => Set<FeedCard>();
    public DbSet<ForumMembership> ForumMemberships => Set<ForumMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FederationState>(e =>
        {
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<RegisteredServer>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ServerDid).IsUnique();
            e.HasIndex(x => x.BaseUrl).IsUnique();
            e.Property(x => x.ServerDid).HasMaxLength(512);
            e.Property(x => x.BaseUrl).HasMaxLength(2048);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.InitiatedBy).HasMaxLength(32);
            e.Property(x => x.OriginInviteId);
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<HubOperator>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Did).IsUnique();
            e.Property(x => x.Did).HasMaxLength(512);
            e.Property(x => x.Username).HasMaxLength(128);
        });

        modelBuilder.Entity<FeedCard>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ServerDid, x.PostId }).IsUnique();
            e.HasIndex(x => new { x.IsDeleted, x.Visibility, x.CreatedAt });
            e.Property(x => x.ServerDid).HasMaxLength(512);
            e.Property(x => x.ForumSlug).HasMaxLength(256);
            e.Property(x => x.ForumName).HasMaxLength(256);
            e.Property(x => x.OriginBaseUrl).HasMaxLength(2048);
            e.Property(x => x.OriginPostUrl).HasMaxLength(2048);
            e.Property(x => x.ImagePreviewUrl).HasMaxLength(2048);
            e.Property(x => x.NsfwCover).HasMaxLength(140);
            e.Property(x => x.AuthorDid).HasMaxLength(512);
            e.Property(x => x.AuthorDisplayName).HasMaxLength(256);
            e.Property(x => x.Visibility).HasMaxLength(32);
        });

        modelBuilder.Entity<ForumMembership>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserDid, x.ServerDid, x.ForumId }).IsUnique();
            e.Property(x => x.UserDid).HasMaxLength(512);
            e.Property(x => x.ServerDid).HasMaxLength(512);
        });
    }

    /// <summary>
    /// EnsureCreated does not add columns to an existing database. Patch so persisted
    /// hub DBs pick up membership request fields without a full reset.
    /// </summary>
    public async Task EnsureMembershipRequestColumnsAsync(CancellationToken ct = default)
    {
        await Database.ExecuteSqlRawAsync(
            """ALTER TABLE "RegisteredServers" ADD COLUMN IF NOT EXISTS "Status" character varying(32) NOT NULL DEFAULT 'active';""",
            ct);
        await Database.ExecuteSqlRawAsync(
            """ALTER TABLE "RegisteredServers" ADD COLUMN IF NOT EXISTS "OriginInviteId" uuid NULL;""",
            ct);
    }

    public async Task EnsureFeedCardSchemaAsync(CancellationToken ct = default)
    {
        await Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "FeedCards" (
                "Id" uuid NOT NULL,
                "ServerDid" character varying(512) NOT NULL,
                "PostId" uuid NOT NULL,
                "ForumId" uuid NOT NULL,
                "ForumSlug" character varying(256) NOT NULL,
                "ForumName" character varying(256) NOT NULL,
                "OriginBaseUrl" character varying(2048) NOT NULL,
                "OriginPostUrl" character varying(2048) NOT NULL,
                "Title" text NULL,
                "Excerpt" text NOT NULL DEFAULT '',
                "ImagePreviewUrl" character varying(2048) NULL,
                "AuthorDid" character varying(512) NOT NULL DEFAULT '',
                "AuthorDisplayName" character varying(256) NOT NULL DEFAULT '',
                "CreatedAt" timestamp with time zone NOT NULL,
                "EditedAt" timestamp with time zone NULL,
                "Visibility" character varying(32) NOT NULL,
                "Score" integer NOT NULL,
                "CommentCount" integer NOT NULL,
                "MapVersion" bigint NOT NULL,
                "SchemaVersion" integer NOT NULL,
                "EventAt" timestamp with time zone NOT NULL,
                "IsDeleted" boolean NOT NULL,
                CONSTRAINT "PK_FeedCards" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FeedCards_ServerDid_PostId" ON "FeedCards" ("ServerDid", "PostId");
            CREATE INDEX IF NOT EXISTS "IX_FeedCards_IsDeleted_Visibility_CreatedAt"
                ON "FeedCards" ("IsDeleted", "Visibility", "CreatedAt");
            ALTER TABLE "FeedCards" ADD COLUMN IF NOT EXISTS "NsfwCover" character varying(140) NULL;
            """,
            ct);
    }

    public async Task EnsureForumMembershipSchemaAsync(CancellationToken ct = default)
    {
        await Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ForumMemberships" (
                "Id" uuid NOT NULL,
                "UserDid" character varying(512) NOT NULL,
                "ServerDid" character varying(512) NOT NULL,
                "ForumId" uuid NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_ForumMemberships" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ForumMemberships_UserDid_ServerDid_ForumId"
                ON "ForumMemberships" ("UserDid", "ServerDid", "ForumId");
            ALTER TABLE "ForumMemberships" ADD COLUMN IF NOT EXISTS "IsFavorite" boolean NOT NULL DEFAULT false;
            """,
            ct);
    }
}
