using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Snekl.Account.Infrastructure.Data;
using Snekl.Account.Infrastructure.Security;
using Snekl.Account.Infrastructure.Services;
using Snekl.Redis;

namespace Snekl.Account.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAccountInfrastructure(this IServiceCollection services, string connectionString, IConfiguration config)
    {
        services.AddDbContext<AccountDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddSneklRedis(config, config["Auth:Name"] ?? "snekl-auth");

        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IAuthServerService, AuthServerService>();
        services.AddScoped<IAccountAuthService, AccountAuthService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<IEmailVerificationService, EmailVerificationService>();
        services.AddScoped<IRingLoginService, RingLoginService>();
        services.AddScoped<IPersonClusterService, PersonClusterService>();
        services.AddScoped<IPartnerService, PartnerService>();
        services.AddScoped<IIdentityCredentialService, IdentityCredentialService>();
        services.AddHttpClient<IPartnerClient, PartnerClient>();
        services.AddHttpClient<IContentServerMembershipService, ContentServerMembershipService>();
        services.AddHttpClient("extension-install");
        services.AddScoped<IContentInstallLookup, ContentInstallLookup>();
        services.AddScoped<IOauthService, OauthService>();

        if (!string.IsNullOrWhiteSpace(config["Smtp:Host"]))
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        else
            services.AddSingleton<IEmailSender, LoggingEmailSender>();

        return services;
    }
}
