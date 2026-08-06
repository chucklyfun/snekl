using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Snekl.Account.Api.Controllers;
using Snekl.Tests.Shared;

namespace Snekl.Account.Api.Tests;

[CollectionDefinition(Name)]
public class AccountApiCollection : ICollectionFixture<AccountApiFixture>
{
    public const string Name = "AccountApi";
}

public sealed class AccountApiFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public WebApplicationFactory<AuthController> Factory { get; private set; } = null!;

    public HttpClient Client => Factory.CreateClient();

    public string AuthBaseUrl { get; } = "http://auth.test";

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        const string dbName = "snekl_auth_tests";
        await PostgresDatabase.EnsureCreatedAsync(_postgres.ConnectionString, dbName);
        var connectionString = _postgres.ConnectionStringForDatabase(dbName);

        Factory = new WebApplicationFactory<AuthController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("ConnectionStrings:Redis", "");
            builder.UseSetting("Auth:BaseUrl", AuthBaseUrl);
            builder.UseSetting("Auth:Name", "Snekl Auth Test");
            builder.UseSetting("Auth:SigningKey", "snekl-auth-test-signing-key-min-32-chars!!");
            builder.UseSetting("Auth:AllowDevPasswordReset", "true");
            builder.UseSetting("Email:AutoVerify", "true");
            builder.UseSetting("RateLimiting:Enabled", "false");
            builder.UseSetting("Snekl:ResetDb", "true");
            builder.UseSetting("Frontend:BaseUrl", "http://localhost:5173");
        });

        // Force host startup (DB ensure created / seed).
        _ = Client;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
