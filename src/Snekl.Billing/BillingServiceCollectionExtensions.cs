using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Snekl.Billing;

public static class BillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Postgres billing book. Call this from the host that charges Stripe. Auth and commissions do not call it yet.
    /// </summary>
    public static IServiceCollection AddSneklBilling(this IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<BillingDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IBillingStore, EfBillingStore>();
        return services;
    }
}
