using System.Text;
using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Identity;
using Snekl.Infrastructure;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;
using Snekl.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=snekl;Username=snekl;Password=snekl";

builder.Services.AddSneklInfrastructure(connectionString, builder.Configuration);

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SneklDbContext>();
    if (app.Environment.IsDevelopment())
    {
        await db.Database.EnsureDeletedAsync();
    }
    await db.Database.EnsureCreatedAsync();

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
