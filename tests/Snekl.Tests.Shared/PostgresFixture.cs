using Testcontainers.PostgreSql;
using Xunit;

namespace Snekl.Tests.Shared;

/// <summary>Shared Postgres container for integration tests. One instance per test collection.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithUsername("snekl")
        .WithPassword("snekl")
        .WithDatabase("snekl_tests")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public string ConnectionStringForDatabase(string database) =>
        ChangeDatabase(ConnectionString, database);

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private static string ChangeDatabase(string connectionString, string database)
    {
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = database
        };
        return builder.ConnectionString;
    }
}
