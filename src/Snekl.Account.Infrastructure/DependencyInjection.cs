using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Snekl.Account.Infrastructure.Data;
using Snekl.Account.Infrastructure.Security;
using Snekl.Account.Infrastructure.Services;

namespace Snekl.Account.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAccountInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AccountDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IAuthServerService, AuthServerService>();
        services.AddScoped<IAccountAuthService, AccountAuthService>();
        services.AddScoped<IRingLoginService, RingLoginService>();
        services.AddScoped<IPersonClusterService, PersonClusterService>();
        services.AddScoped<IPartnerService, PartnerService>();
        services.AddScoped<IIdentityCredentialService, IdentityCredentialService>();
        services.AddHttpClient<IPartnerClient, PartnerClient>();

        return services;
    }
}
