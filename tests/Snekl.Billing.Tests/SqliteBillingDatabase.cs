using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Snekl.Billing.Tests;

public sealed class SqliteBillingDatabase : IAsyncDisposable
{
    readonly SqliteConnection _connection;

    public SqliteBillingDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<BillingDbContext>().UseSqlite(_connection).Options;
        using (var db = new BillingDbContext(options)) db.Database.EnsureCreated();
        Store = new EfBillingStore(new Factory(options));
    }

    public IBillingStore Store { get; }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    sealed class Factory(DbContextOptions<BillingDbContext> options) : IDbContextFactory<BillingDbContext>
    {
        public BillingDbContext CreateDbContext() => new(options);
    }
}
