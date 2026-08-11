using Microsoft.EntityFrameworkCore;
using Snekl.Api.Realtime;
using Snekl.Domain.Identity;
using Snekl.Infrastructure;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;
using Snekl.Redis;
using Snekl.DevSession;

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
    await db.Database.ExecuteSqlRawAsync("""
        ALTER TABLE "ListEntries" ADD COLUMN IF NOT EXISTS "Label" text NOT NULL DEFAULT '';
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
        """);

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

    var adminAuth = scope.ServiceProvider.GetRequiredService<ISiteAdminAuthService>();
    var adminUser = builder.Configuration["Admin:Username"] ?? "admin";
    var adminPass = builder.Configuration["Admin:Password"] ?? "admin";
    await adminAuth.EnsureSeedAdminAsync(adminUser, adminPass);

    var roleSeed = scope.ServiceProvider.GetRequiredService<IRoleSeedService>();
    await roleSeed.EnsureServerRolesAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseSneklRateLimiting(builder.Configuration);
app.UseAuthentication();
app.UseAuthorization();
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
