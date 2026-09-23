using Microsoft.EntityFrameworkCore;
using Snekl.Api.Realtime;
using Snekl.Domain.Identity;
using Snekl.Infrastructure;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;
using Snekl.Redis;
using Snekl.DevSession;
using Snekl.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=snekl;Username=snekl;Password=snekl";

builder.Services.AddSneklInfrastructure(connectionString, builder.Configuration);

var signalRBuilder = builder.Services.AddSignalR();
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    signalRBuilder.AddStackExchangeRedis(redisConnection);
}

builder.Services.AddSingleton<IRealtimePublisher, SignalRRealtimePublisher>();

builder.Services.AddCors(options =>
{
    // SignalR's JS client sends credentials on negotiate; AllowAnyOrigin (*) is
    // incompatible with that. Reflect the request origin instead.
    options.AddDefaultPolicy(policy =>
        policy.SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseDevSessionTracking("Snekl.Api");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SneklDbContext>();
    if (app.Environment.IsDevelopment() && builder.Configuration.GetValue("Snekl:ResetDb", false))
    {
        await db.Database.EnsureDeletedAsync();
    }
    await db.Database.EnsureCreatedAsync();
    await db.Database.OpenConnectionAsync();
    await using (var schemaCmd = db.Database.GetDbConnection().CreateCommand())
    {
        schemaCmd.CommandText = """
        ALTER TABLE "ListEntries" ADD COLUMN IF NOT EXISTS "Label" text NOT NULL DEFAULT '';
        ALTER TABLE "ListSubscriptions" ADD COLUMN IF NOT EXISTS "Priority" integer NOT NULL DEFAULT 0;
        CREATE TABLE IF NOT EXISTS "ListEntryIgnores" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
            "ListId" uuid NOT NULL REFERENCES "CuratedLists"("Id") ON DELETE CASCADE,
            "TargetUserId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
            "CreatedAt" timestamp with time zone NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_ListEntryIgnores_UserId_ListId_TargetUserId"
            ON "ListEntryIgnores" ("UserId", "ListId", "TargetUserId");
        ALTER TABLE "Posts" ADD COLUMN IF NOT EXISTS "IsDeleted" boolean NOT NULL DEFAULT false;
        ALTER TABLE "Posts" ADD COLUMN IF NOT EXISTS "DeletedAt" timestamp with time zone NULL;
        ALTER TABLE "Posts" ADD COLUMN IF NOT EXISTS "EditedAt" timestamp with time zone NULL;
        ALTER TABLE "NodeConfig" ADD COLUMN IF NOT EXISTS "VerificationKeyMultibase" text NOT NULL DEFAULT '';
        ALTER TABLE "NodeConfig" ADD COLUMN IF NOT EXISTS "VerificationPrivateKeyPkcs8" text NOT NULL DEFAULT '';
        ALTER TABLE "PeerNodes" ADD COLUMN IF NOT EXISTS "RemoteDid" text NOT NULL DEFAULT '';
        ALTER TABLE "PeerNodes" ADD COLUMN IF NOT EXISTS "VerificationKeyMultibase" text NOT NULL DEFAULT '';
        CREATE TABLE IF NOT EXISTS "PostRevisions" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "PostId" uuid NOT NULL REFERENCES "Posts"("Id") ON DELETE CASCADE,
            "EditedByUserId" uuid NOT NULL REFERENCES "Users"("Id"),
            "Message" text NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_PostRevisions_PostId_CreatedAt" ON "PostRevisions" ("PostId", "CreatedAt");
        CREATE TABLE IF NOT EXISTS "ChangeLogEntries" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "Scope" character varying(32) NOT NULL,
            "ForumId" uuid NULL,
            "WatercoolerId" uuid NULL,
            "EntityType" character varying(64) NOT NULL,
            "EntityId" uuid NOT NULL,
            "Action" character varying(32) NOT NULL,
            "ActorUserId" uuid NULL REFERENCES "Users"("Id") ON DELETE SET NULL,
            "Summary" character varying(512) NOT NULL,
            "BeforeJson" text NULL,
            "AfterJson" text NULL,
            "CreatedAt" timestamp with time zone NOT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_ChangeLogEntries_Scope_CreatedAt" ON "ChangeLogEntries" ("Scope", "CreatedAt");
        CREATE INDEX IF NOT EXISTS "IX_ChangeLogEntries_ForumId_CreatedAt" ON "ChangeLogEntries" ("ForumId", "CreatedAt");
        CREATE INDEX IF NOT EXISTS "IX_ChangeLogEntries_WatercoolerId_CreatedAt" ON "ChangeLogEntries" ("WatercoolerId", "CreatedAt");
        UPDATE "ForumRoles"
        SET "Modes" = "Modes" || '{"ViewHistory":"Direct"}'::jsonb
        WHERE COALESCE("Modes"->>'ManagePosts', '') = 'Direct'
          AND COALESCE("Modes"->>'ViewHistory', '') = '';
        UPDATE "ServerRoles"
        SET "Modes" = "Modes" || '{"ViewHistory":"Direct"}'::jsonb
        WHERE (
            COALESCE("Modes"->>'ManageServerRoles', '') = 'Direct'
            OR COALESCE("Modes"->>'ManageServerMembers', '') = 'Direct'
            OR COALESCE("Modes"->>'ManageServerSettings', '') = 'Direct'
          )
          AND COALESCE("Modes"->>'ViewHistory', '') = '';
        UPDATE "WatercoolerRoles"
        SET "Modes" = "Modes" || '{"ViewHistory":"Direct"}'::jsonb
        WHERE COALESCE("Modes"->>'ManageWatercooler', '') = 'Direct'
          AND COALESCE("Modes"->>'ViewHistory', '') = '';
        ALTER TABLE "Posts" ADD COLUMN IF NOT EXISTS "Title" text NULL;
        CREATE TABLE IF NOT EXISTS "MediaAssets" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "UploaderUserId" uuid NOT NULL REFERENCES "Users"("Id"),
            "StorageKey" text NOT NULL,
            "ExternalUrl" text NULL,
            "ContentType" text NOT NULL,
            "ByteSize" bigint NOT NULL,
            "Width" integer NULL,
            "Height" integer NULL,
            "CreatedAt" timestamp with time zone NOT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_MediaAssets_StorageKey" ON "MediaAssets" ("StorageKey");
        ALTER TABLE "MediaAssets" ADD COLUMN IF NOT EXISTS "ExternalUrl" text NULL;
        CREATE INDEX IF NOT EXISTS "IX_MediaAssets_UploaderUserId" ON "MediaAssets" ("UploaderUserId");
        CREATE TABLE IF NOT EXISTS "PostMedia" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "PostId" uuid NOT NULL REFERENCES "Posts"("Id") ON DELETE CASCADE,
            "MediaAssetId" uuid NOT NULL REFERENCES "MediaAssets"("Id") ON DELETE CASCADE,
            "SortOrder" integer NOT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_PostMedia_PostId_SortOrder" ON "PostMedia" ("PostId", "SortOrder");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_PostMedia_MediaAssetId" ON "PostMedia" ("MediaAssetId");
        CREATE TABLE IF NOT EXISTS "HubInvites" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "HubBaseUrl" character varying(2048) NOT NULL,
            "HubName" character varying(256) NOT NULL,
            "RequestedByDid" character varying(512) NOT NULL,
            "RequestedByUsername" character varying(128) NOT NULL,
            "Status" character varying(32) NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_HubInvites_Status" ON "HubInvites" ("Status");
        ALTER TABLE "SiteAdmins" ADD COLUMN IF NOT EXISTS "UserId" uuid NULL;
        ALTER TABLE "SiteAdmins" ADD COLUMN IF NOT EXISTS "Did" text NULL;
        ALTER TABLE "SiteAdmins" ADD COLUMN IF NOT EXISTS "InvitedByAdminId" uuid NULL;
        ALTER TABLE "SiteAdmins" ALTER COLUMN "PasswordHash" DROP NOT NULL;
        ALTER TABLE "Forums" ADD COLUMN IF NOT EXISTS "ApprovalStatus" character varying(32) NOT NULL DEFAULT 'approved';
        ALTER TABLE "ServerRoles" ADD COLUMN IF NOT EXISTS "Modes" jsonb NOT NULL DEFAULT '{{}}'::jsonb;
        ALTER TABLE "ForumRoles" ADD COLUMN IF NOT EXISTS "Modes" jsonb NOT NULL DEFAULT '{{}}'::jsonb;
        DO $$
        BEGIN
          IF EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'ServerRoles' AND column_name = 'Permissions'
          ) THEN
            UPDATE "ServerRoles"
            SET "Modes" = CASE
                WHEN (("Permissions"::bigint) & 256) <> 0 AND (("Permissions"::bigint) & 2) = 0 THEN
                    jsonb_strip_nulls(jsonb_build_object(
                        'ViewServer', CASE WHEN (("Permissions"::bigint) & 1) <> 0 THEN 'Direct' END,
                        'CreateForum', 'Request',
                        'ManageServerMembers', CASE WHEN (("Permissions"::bigint) & 4) <> 0 THEN 'Direct' END,
                        'ManageServerRoles', CASE WHEN (("Permissions"::bigint) & 8) <> 0 THEN 'Direct' END,
                        'ManageServerSettings', CASE WHEN (("Permissions"::bigint) & 16) <> 0 THEN 'Direct' END,
                        'ServerAdministrator', CASE WHEN (("Permissions"::bigint) & 32) <> 0 THEN 'Direct' END,
                        'RequestFederation', CASE WHEN (("Permissions"::bigint) & 64) <> 0 THEN 'Direct' END,
                        'AcceptFederation', CASE WHEN (("Permissions"::bigint) & 128) <> 0 THEN 'Direct' END
                    ))
                ELSE
                    jsonb_strip_nulls(jsonb_build_object(
                        'ViewServer', CASE WHEN (("Permissions"::bigint) & 1) <> 0 THEN 'Direct' END,
                        'CreateForum', CASE WHEN (("Permissions"::bigint) & 2) <> 0 THEN 'Direct' END,
                        'ManageServerMembers', CASE WHEN (("Permissions"::bigint) & 4) <> 0 THEN 'Direct' END,
                        'ManageServerRoles', CASE WHEN (("Permissions"::bigint) & 8) <> 0 THEN 'Direct' END,
                        'ManageServerSettings', CASE WHEN (("Permissions"::bigint) & 16) <> 0 THEN 'Direct' END,
                        'ServerAdministrator', CASE WHEN (("Permissions"::bigint) & 32) <> 0 THEN 'Direct' END,
                        'RequestFederation', CASE WHEN (("Permissions"::bigint) & 64) <> 0 THEN 'Direct' END,
                        'AcceptFederation', CASE WHEN (("Permissions"::bigint) & 128) <> 0 THEN 'Direct' END
                    ))
                END
            WHERE "Modes" = '{{}}'::jsonb;
          END IF;

          IF EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'ForumRoles' AND column_name = 'Permissions'
          ) THEN
            UPDATE "ForumRoles"
            SET "Modes" = jsonb_strip_nulls(jsonb_build_object(
                'ViewForum', CASE WHEN (("Permissions"::bigint) & 1) <> 0 THEN 'Direct' END,
                'SeeForum', CASE WHEN (("Permissions"::bigint) & 1) <> 0 OR (("Permissions"::bigint) & 1024) <> 0 THEN 'Direct' END,
                'CreateThread', CASE WHEN (("Permissions"::bigint) & 2) <> 0 THEN 'Direct' END,
                'Reply', CASE WHEN (("Permissions"::bigint) & 4) <> 0 THEN 'Direct' END,
                'EditOwnPost', CASE WHEN (("Permissions"::bigint) & 8) <> 0 THEN 'Direct' END,
                'DeleteOwnPost', CASE WHEN (("Permissions"::bigint) & 16) <> 0 THEN 'Direct' END,
                'ManagePosts', CASE WHEN (("Permissions"::bigint) & 32) <> 0 THEN 'Direct' END,
                'ManageMembers', CASE WHEN (("Permissions"::bigint) & 64) <> 0 THEN 'Direct' END,
                'ManageRoles', CASE WHEN (("Permissions"::bigint) & 128) <> 0 THEN 'Direct' END,
                'ManageForum', CASE WHEN (("Permissions"::bigint) & 256) <> 0 THEN 'Direct' END,
                'ForumAdministrator', CASE WHEN (("Permissions"::bigint) & 512) <> 0 THEN 'Direct' END
            ))
            WHERE "Modes" = '{{}}'::jsonb;
          END IF;
        END $$;
        ALTER TABLE "ServerRoles" DROP COLUMN IF EXISTS "Permissions";
        ALTER TABLE "ForumRoles" DROP COLUMN IF EXISTS "Permissions";
        ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "IsBot" boolean NOT NULL DEFAULT false;
        ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "OAuthClientId" text NULL;
        ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "ExtensionInstallId" uuid NULL;
        CREATE TABLE IF NOT EXISTS "ExtensionInstalls" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "ClientId" character varying(128) NOT NULL,
            "AppName" character varying(256) NOT NULL,
            "AdminUrl" text NULL,
            "IframeUrl" text NULL,
            "InstalledByUserId" uuid NOT NULL REFERENCES "Users"("Id"),
            "InstalledByDid" text NOT NULL DEFAULT '',
            "ResourceType" character varying(32) NOT NULL,
            "ForumId" uuid NULL REFERENCES "Forums"("Id"),
            "ServerPermissions" bigint NOT NULL DEFAULT 0,
            "ForumPermissions" bigint NOT NULL DEFAULT 0,
            "CreatedAt" timestamp with time zone NOT NULL,
            "RevokedAt" timestamp with time zone NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_ExtensionInstalls_ClientId" ON "ExtensionInstalls" ("ClientId");
        WITH ranked AS (
            SELECT "Id", "ClientId",
                   ROW_NUMBER() OVER (PARTITION BY "ClientId" ORDER BY "CreatedAt" DESC, "Id" DESC) AS rn
            FROM "ExtensionInstalls"
            WHERE "RevokedAt" IS NULL
        )
        UPDATE "Users" AS u
        SET "ExtensionInstallId" = k."Id"
        FROM ranked AS d
        JOIN ranked AS k ON k."ClientId" = d."ClientId" AND k.rn = 1
        WHERE d.rn > 1
          AND u."ExtensionInstallId" = d."Id";
        WITH ranked AS (
            SELECT "Id",
                   ROW_NUMBER() OVER (PARTITION BY "ClientId" ORDER BY "CreatedAt" DESC, "Id" DESC) AS rn
            FROM "ExtensionInstalls"
            WHERE "RevokedAt" IS NULL
        )
        UPDATE "ExtensionInstalls" AS i
        SET "RevokedAt" = NOW()
        FROM ranked AS r
        WHERE i."Id" = r."Id"
          AND r.rn > 1
          AND i."RevokedAt" IS NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_ExtensionInstalls_ClientId_Active"
            ON "ExtensionInstalls" ("ClientId")
            WHERE "RevokedAt" IS NULL;
        CREATE TABLE IF NOT EXISTS "ForumMemberships" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "ForumId" uuid NOT NULL REFERENCES "Forums"("Id") ON DELETE CASCADE,
            "UserId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
            "IsFavorite" boolean NOT NULL DEFAULT false,
            "UpdatedAt" timestamp with time zone NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_ForumMemberships_ForumId_UserId"
            ON "ForumMemberships" ("ForumId", "UserId");
        CREATE TABLE IF NOT EXISTS "HubOutbox" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "HubBaseUrl" character varying(2048) NOT NULL,
            "Path" character varying(256) NOT NULL,
            "EnvelopeType" character varying(64) NOT NULL,
            "PayloadJson" text NOT NULL,
            "Attempts" integer NOT NULL,
            "NextAttemptAt" timestamp with time zone NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "DeliveredAt" timestamp with time zone NULL,
            "LastError" character varying(2048) NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_HubOutbox_DeliveredAt_NextAttemptAt"
            ON "HubOutbox" ("DeliveredAt", "NextAttemptAt");
        CREATE TABLE IF NOT EXISTS "Emotes" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "Code" character varying(32) NOT NULL,
            "Name" character varying(64) NOT NULL,
            "MediaAssetId" uuid NOT NULL REFERENCES "MediaAssets"("Id") ON DELETE CASCADE,
            "ForumId" uuid NULL REFERENCES "Forums"("Id") ON DELETE CASCADE,
            "CreatedByUserId" uuid NOT NULL REFERENCES "Users"("Id"),
            "IsAnimated" boolean NOT NULL DEFAULT false,
            "CreatedAt" timestamp with time zone NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Emotes_MediaAssetId" ON "Emotes" ("MediaAssetId");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Emotes_ForumId_Code" ON "Emotes" ("ForumId", "Code");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Emotes_Server_Code" ON "Emotes" ("Code") WHERE "ForumId" IS NULL;
        ALTER TABLE "Emotes" ADD COLUMN IF NOT EXISTS "SkinsUnicode" character varying(16) NULL;
        ALTER TABLE "Emotes" ALTER COLUMN "MediaAssetId" DROP NOT NULL;
        ALTER TABLE "Emotes" ADD COLUMN IF NOT EXISTS "SortOrder" integer NOT NULL DEFAULT 0;
        ALTER TABLE "Emotes" ADD COLUMN IF NOT EXISTS "Highlighted" boolean NOT NULL DEFAULT false;
        DROP INDEX IF EXISTS "IX_Emotes_MediaAssetId";
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Emotes_MediaAssetId" ON "Emotes" ("MediaAssetId") WHERE "MediaAssetId" IS NOT NULL;
        ALTER TABLE "Forums" ADD COLUMN IF NOT EXISTS "EmoteDefaultsSeeded" boolean NOT NULL DEFAULT false;
        ALTER TABLE "NodeConfig" ADD COLUMN IF NOT EXISTS "EmoteDefaultsSeeded" boolean NOT NULL DEFAULT false;
        CREATE TABLE IF NOT EXISTS "PostReactions" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "PostId" uuid NOT NULL REFERENCES "Posts"("Id") ON DELETE CASCADE,
            "UserId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
            "EmoteId" uuid NULL REFERENCES "Emotes"("Id") ON DELETE CASCADE,
            "Emoji" character varying(16) NULL,
            "CreatedAt" timestamp with time zone NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_PostReactions_PostId_UserId_EmoteId"
            ON "PostReactions" ("PostId", "UserId", "EmoteId") WHERE "EmoteId" IS NOT NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_PostReactions_PostId_UserId_Emoji"
            ON "PostReactions" ("PostId", "UserId", "Emoji") WHERE "Emoji" IS NOT NULL;
        CREATE INDEX IF NOT EXISTS "IX_PostReactions_PostId" ON "PostReactions" ("PostId");
        CREATE TABLE IF NOT EXISTS "ContentBlockTypes" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "Key" character varying(64) NOT NULL,
            "Name" character varying(128) NOT NULL,
            "Description" text NULL,
            "Position" integer NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_ContentBlockTypes_Key" ON "ContentBlockTypes" ("Key");
        CREATE TABLE IF NOT EXISTS "ContentBlockPermissions" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "BlockTypeId" uuid NOT NULL REFERENCES "ContentBlockTypes"("Id") ON DELETE CASCADE,
            "Name" character varying(128) NOT NULL,
            "Label" character varying(256) NOT NULL,
            "Description" text NULL,
            "Scope" character varying(32) NOT NULL,
            "Position" integer NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_ContentBlockPermissions_BlockTypeId_Name_Scope"
            ON "ContentBlockPermissions" ("BlockTypeId", "Name", "Scope");
        ALTER TABLE "Posts" ALTER COLUMN "ForumId" DROP NOT NULL;
        ALTER TABLE "Posts" ADD COLUMN IF NOT EXISTS "WatercoolerId" uuid NULL;
        ALTER TABLE "Posts" ADD COLUMN IF NOT EXISTS "QuotedPostId" uuid NULL;
        ALTER TABLE "Posts" ADD COLUMN IF NOT EXISTS "QuoteCount" integer NOT NULL DEFAULT 0;
        CREATE TABLE IF NOT EXISTS "Watercoolers" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "Name" text NOT NULL,
            "Slug" text NOT NULL,
            "Description" text NOT NULL,
            "CreatedByUserId" uuid NOT NULL REFERENCES "Users"("Id"),
            "ParentForumId" uuid NULL REFERENCES "Forums"("Id") ON DELETE CASCADE,
            "ApprovalStatus" character varying(32) NOT NULL DEFAULT 'approved',
            "AllowUpvotes" boolean NOT NULL,
            "NewestAtBottom" boolean NOT NULL DEFAULT true,
            "CreatedAt" timestamp with time zone NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            "OriginNodeId" uuid NULL,
            "OriginNodeUrl" text NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Watercoolers_ParentForumId_Slug"
            ON "Watercoolers" ("ParentForumId", "Slug");
        CREATE TABLE IF NOT EXISTS "WatercoolerRoles" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "WatercoolerId" uuid NOT NULL REFERENCES "Watercoolers"("Id") ON DELETE CASCADE,
            "Name" text NOT NULL,
            "Color" text NULL,
            "Position" integer NOT NULL,
            "Modes" jsonb NOT NULL DEFAULT '{{}}'::jsonb,
            "IsEveryone" boolean NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL
        );
        CREATE TABLE IF NOT EXISTS "WatercoolerRoleAssignments" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "WatercoolerId" uuid NOT NULL REFERENCES "Watercoolers"("Id") ON DELETE CASCADE,
            "UserId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
            "RoleId" uuid NOT NULL REFERENCES "WatercoolerRoles"("Id") ON DELETE CASCADE,
            "AssignedAt" timestamp with time zone NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_WatercoolerRoleAssignments_WatercoolerId_UserId_RoleId"
            ON "WatercoolerRoleAssignments" ("WatercoolerId", "UserId", "RoleId");
        CREATE TABLE IF NOT EXISTS "ForumContentBlocks" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "ForumId" uuid NOT NULL REFERENCES "Forums"("Id") ON DELETE CASCADE,
            "TypeKey" character varying(64) NOT NULL,
            "WatercoolerId" uuid NULL REFERENCES "Watercoolers"("Id") ON DELETE CASCADE,
            "Slot" character varying(32) NOT NULL,
            "Position" integer NOT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_ForumContentBlocks_ForumId_Slot_Position"
            ON "ForumContentBlocks" ("ForumId", "Slot", "Position");
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_constraint WHERE conname = 'FK_Posts_Watercoolers_WatercoolerId'
            ) THEN
                ALTER TABLE "Posts"
                    ADD CONSTRAINT "FK_Posts_Watercoolers_WatercoolerId"
                    FOREIGN KEY ("WatercoolerId") REFERENCES "Watercoolers"("Id");
            END IF;
        END $$;
        INSERT INTO "ForumMemberships" ("Id", "ForumId", "UserId", "IsFavorite", "UpdatedAt")
        SELECT gen_random_uuid(), a."ForumId", a."UserId", false, NOW()
        FROM (SELECT DISTINCT "ForumId", "UserId" FROM "ForumRoleAssignments") a
        WHERE NOT EXISTS (
            SELECT 1 FROM "ForumMemberships" m
            WHERE m."ForumId" = a."ForumId" AND m."UserId" = a."UserId"
        );
        ALTER TABLE "NodeConfig" ADD COLUMN IF NOT EXISTS "NsfwAgeCheckRequired" boolean NOT NULL DEFAULT false;
        ALTER TABLE "NodeConfig" ADD COLUMN IF NOT EXISTS "NsfwMinimumAge" integer NOT NULL DEFAULT 18;
        ALTER TABLE "Forums" ADD COLUMN IF NOT EXISTS "NsfwAgeCheckRequired" boolean NOT NULL DEFAULT false;
        ALTER TABLE "Forums" ADD COLUMN IF NOT EXISTS "NsfwAgeCheckExplanation" character varying(2000) NOT NULL DEFAULT '';
        ALTER TABLE "Forums" ADD COLUMN IF NOT EXISTS "AvatarMediaAssetId" uuid NULL;
        ALTER TABLE "Forums" ADD COLUMN IF NOT EXISTS "BannerMediaAssetId" uuid NULL;
        ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "NsfwAgeAttestedAt" timestamp with time zone NULL;
        ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "NsfwAgeAttestedMinimum" integer NULL;
        ALTER TABLE "PostMedia" ADD COLUMN IF NOT EXISTS "IsNsfw" boolean NOT NULL DEFAULT false;
        ALTER TABLE "PostMedia" ADD COLUMN IF NOT EXISTS "CoverText" character varying(140) NULL;
        ALTER TABLE "PostMedia" ADD COLUMN IF NOT EXISTS "CensorStorageKey" text NULL;
        ALTER TABLE "PostMedia" ADD COLUMN IF NOT EXISTS "CensorContentType" text NULL;
        ALTER TABLE "PostMedia" ADD COLUMN IF NOT EXISTS "CensorByteSize" bigint NULL;
        CREATE TABLE IF NOT EXISTS "DirectConversations" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "UserLowId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE RESTRICT,
            "UserHighId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE RESTRICT,
            "OriginForumId" uuid NULL REFERENCES "Forums"("Id") ON DELETE SET NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_DirectConversations_UserLowId_UserHighId"
            ON "DirectConversations" ("UserLowId", "UserHighId");
        CREATE TABLE IF NOT EXISTS "DirectConversationRequests" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "RequesterUserId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE RESTRICT,
            "TargetUserId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE RESTRICT,
            "ForumId" uuid NOT NULL REFERENCES "Forums"("Id") ON DELETE CASCADE,
            "Message" character varying(2000) NULL,
            "Status" character varying(32) NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "ResolvedAt" timestamp with time zone NULL,
            "ConversationId" uuid NULL REFERENCES "DirectConversations"("Id") ON DELETE SET NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_DirectConversationRequests_TargetUserId_Status_CreatedAt"
            ON "DirectConversationRequests" ("TargetUserId", "Status", "CreatedAt");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_DirectConversationRequests_PendingPair"
            ON "DirectConversationRequests" ("RequesterUserId", "TargetUserId")
            WHERE "Status" = 'pending';
        CREATE TABLE IF NOT EXISTS "DirectMessages" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "ConversationId" uuid NOT NULL REFERENCES "DirectConversations"("Id") ON DELETE CASCADE,
            "AuthorUserId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE RESTRICT,
            "Body" character varying(8000) NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_DirectMessages_ConversationId_CreatedAt"
            ON "DirectMessages" ("ConversationId", "CreatedAt");
        CREATE TABLE IF NOT EXISTS "DirectMessageForumPreferences" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
            "ForumId" uuid NOT NULL REFERENCES "Forums"("Id") ON DELETE CASCADE,
            "Mode" character varying(16) NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_DirectMessageForumPreferences_UserId_ForumId"
            ON "DirectMessageForumPreferences" ("UserId", "ForumId");
        ALTER TABLE "Posts" ADD COLUMN IF NOT EXISTS "ReplyPolicy" text NOT NULL DEFAULT 'everyone';
        ALTER TABLE "NodeConfig" ADD COLUMN IF NOT EXISTS "OwnerUserId" uuid NULL;
        ALTER TABLE "Forums" ADD COLUMN IF NOT EXISTS "OwnerUserId" uuid NULL;
        ALTER TABLE "ForumRoles" ADD COLUMN IF NOT EXISTS "IsOwner" boolean NOT NULL DEFAULT false;
        UPDATE "ForumRoles" SET "IsOwner" = true
            WHERE "IsOwner" = false AND lower("Name") = 'owner';
        UPDATE "Forums" f SET "OwnerUserId" = sub."UserId"
            FROM (
                SELECT a."ForumId", a."UserId"
                FROM "ForumRoleAssignments" a
                INNER JOIN "ForumRoles" r ON r."Id" = a."RoleId"
                WHERE r."IsOwner" = true
            ) sub
            WHERE f."Id" = sub."ForumId" AND f."OwnerUserId" IS NULL;
        UPDATE "Forums" SET "OwnerUserId" = "CreatedByUserId"
            WHERE "OwnerUserId" IS NULL AND "CreatedByUserId" IS NOT NULL;
        UPDATE "NodeConfig" n SET "OwnerUserId" = sub."UserId"
            FROM (
                SELECT "UserId" FROM "SiteAdmins"
                WHERE "UserId" IS NOT NULL
                ORDER BY "CreatedAt" ASC
                LIMIT 1
            ) sub
            WHERE n."OwnerUserId" IS NULL;
        UPDATE "NodeConfig" n SET "OwnerUserId" = sub."UserId"
            FROM (
                SELECT "UserId" FROM "SuperUserGrants"
                ORDER BY "GrantedAt" ASC
                LIMIT 1
            ) sub
            WHERE n."OwnerUserId" IS NULL;
        """;
        await schemaCmd.ExecuteNonQueryAsync();
    }

    var nodeService = scope.ServiceProvider.GetRequiredService<INodeService>();
    var node = await nodeService.GetOrCreateAsync();
    var configuredUrl = builder.Configuration["Node:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(configuredUrl) && node.BaseUrl != configuredUrl)
    {
        await nodeService.UpdateAsync(
            builder.Configuration["Node:Name"] ?? node.Name,
            configuredUrl,
            builder.Configuration["Node:Description"] ?? node.Description);
    }

    await SeedTrustedAuthIssuersAsync(db, builder.Configuration);

    var roleSeed = scope.ServiceProvider.GetRequiredService<IRoleSeedService>();
    await roleSeed.EnsureServerRolesAsync();
    var blockCatalog = scope.ServiceProvider.GetRequiredService<IContentBlockCatalogService>();
    await blockCatalog.EnsureSeededAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSneklForwardedHeaders();
app.UseCors();
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/admin" || context.Request.Path == "/admin/")
        context.Request.Path = "/admin/index.html";
    await next();
});
app.UseStaticFiles();
app.UseSneklRateLimiting(builder.Configuration);
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (InstallTokenGuard.IsBlocked(context))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "Install tokens cannot act as the authorizing user." });
        return;
    }

    if (BotCapabilityGuard.IsBlocked(context))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Bots can only call Content APIs covered by the extension install grant."
        });
        return;
    }

    await next();
});
app.MapControllers();
app.MapHub<ThreadHub>("/hubs/thread");

app.Run();

static async Task SeedTrustedAuthIssuersAsync(SneklDbContext db, ConfigurationManager config)
{
    var configured = config.GetSection("AuthIssuers").GetChildren().ToList();
    var entries = configured.Count > 0
        ? configured.Select(c => (Name: c["Name"] ?? c["IssuerUrl"]!, IssuerUrl: c["IssuerUrl"]!, JwksUrl: c["JwksUrl"]!))
        :
        [
            ("Snekl Auth Alpha", "http://localhost:5100", "http://localhost:5100/.well-known/jwks.json"),
            ("Snekl Auth Beta", "http://localhost:5101", "http://localhost:5101/.well-known/jwks.json")
        ];

    foreach (var (name, issuerUrl, jwksUrl) in entries)
    {
        if (string.IsNullOrWhiteSpace(issuerUrl) || string.IsNullOrWhiteSpace(jwksUrl)) continue;
        var normalized = issuerUrl.Trim().TrimEnd('/');
        var existing = await db.TrustedAuthIssuers.FirstOrDefaultAsync(i => i.IssuerUrl == normalized);
        if (existing is null)
        {
            db.TrustedAuthIssuers.Add(new TrustedAuthIssuer
            {
                IssuerUrl = normalized,
                JwksUrl = jwksUrl.Trim(),
                Name = name
            });
        }
        else
        {
            existing.JwksUrl = jwksUrl.Trim();
            existing.Name = name;
            existing.IsActive = true;
        }
    }

    await db.SaveChangesAsync();
}

public partial class Program;
