using Npgsql;

namespace Snekl.Tests.Shared;

public static class PostgresDatabase
{
    public static async Task EnsureCreatedAsync(string adminConnectionString, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = "postgres"
        };

        await using var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync();

        await using (var exists = new NpgsqlCommand(
                         "SELECT 1 FROM pg_database WHERE datname = @name", conn))
        {
            exists.Parameters.AddWithValue("name", databaseName);
            if (await exists.ExecuteScalarAsync() is not null)
                return;
        }

        await using var create = new NpgsqlCommand(
            $"CREATE DATABASE \"{databaseName.Replace("\"", "\"\"")}\"", conn);
        await create.ExecuteNonQueryAsync();
    }
}
