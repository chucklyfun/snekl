using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Snekl.Api.Controllers;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;
using Snekl.Tests.Shared;

namespace Snekl.Api.Tests;

[CollectionDefinition(Name)]
public class ContentApiCollection : ICollectionFixture<ContentApiFixture>
{
    public const string Name = "ContentApi";
}

public sealed class ContentApiFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public WebApplicationFactory<ForumsController> Factory { get; private set; } = null!;

    public HttpClient Client => Factory.CreateClient();

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        const string dbName = "snekl_content_tests";
        await PostgresDatabase.EnsureCreatedAsync(_postgres.ConnectionString, dbName);
        var connectionString = _postgres.ConnectionStringForDatabase(dbName);

        Factory = new WebApplicationFactory<ForumsController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("ConnectionStrings:Redis", "");
            builder.UseSetting("ConnectionStrings:OpenSearch", "");
            builder.UseSetting("RateLimiting:Enabled", "false");
            builder.UseSetting("Snekl:ResetDb", "true");
            builder.UseSetting("Node:Name", "Snekl Test Node");
            builder.UseSetting("Node:BaseUrl", "http://content.test");
            builder.UseSetting("Node:Description", "Test node");
            builder.UseSetting("Jwt:Key", "snekl-content-test-secret-key-min-32-chars");
            builder.UseSetting("Jwt:Issuer", "snekl");
            builder.UseSetting("Jwt:Audience", "snekl");
            builder.UseSetting("AuthIssuers:0:Name", "Unused");
            builder.UseSetting("AuthIssuers:0:IssuerUrl", "http://unused.invalid");
            builder.UseSetting("AuthIssuers:0:JwksUrl", "http://unused.invalid/jwks");
            builder.UseSetting("ObjectStorage:Provider", "InMemory");
            builder.UseSetting("ObjectStorage:MaxUploadBytes", "10485760");
        });

        _ = Client;
    }

    public async Task GrantMemberRoleAsync(Guid userId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SneklDbContext>();
        var member = await db.ServerRoles.FirstAsync(r => r.Name == "Member");
        if (!await db.ServerRoleAssignments.AnyAsync(a => a.UserId == userId && a.RoleId == member.Id))
        {
            db.ServerRoleAssignments.Add(new ServerRoleAssignment
            {
                UserId = userId,
                RoleId = member.Id
            });
            await db.SaveChangesAsync();
        }
    }

    public async Task GrantForumRequesterRoleAsync(Guid userId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SneklDbContext>();
        var role = await db.ServerRoles.FirstAsync(r => r.Name == "Nest requester");
        if (!await db.ServerRoleAssignments.AnyAsync(a => a.UserId == userId && a.RoleId == role.Id))
        {
            db.ServerRoleAssignments.Add(new ServerRoleAssignment
            {
                UserId = userId,
                RoleId = role.Id
            });
            await db.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
