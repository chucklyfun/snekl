using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Snekl.Domain;
using Snekl.Domain.Security;

namespace Snekl.DeviceBinding;

public static class DeviceProofAuth
{
    public static IServiceCollection AddDeviceProof(this IServiceCollection services)
    {
        services.AddSingleton<DeviceProofReplayCache>();
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, AcceptBoundTokens);
        return services;
    }

    public static void AcceptBoundTokens(JwtBearerOptions options)
    {
        var prior = options.Events?.OnMessageReceived;
        options.Events ??= new JwtBearerEvents();
        options.Events.OnMessageReceived = async context =>
        {
            if (prior is not null)
                await prior(context);

            var authorization = context.Request.Headers.Authorization.ToString();
            if (authorization.StartsWith("DPoP ", StringComparison.OrdinalIgnoreCase))
                context.Token = authorization["DPoP ".Length..].Trim();
        };
    }

    public static IApplicationBuilder UseDeviceProof(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var thumbprint = context.User.FindFirst(OAuthClaims.CnfJkt)?.Value;
            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                await next();
                return;
            }

            var authorization = context.Request.Headers.Authorization.ToString();
            var token = authorization.StartsWith("DPoP ", StringComparison.OrdinalIgnoreCase)
                ? authorization["DPoP ".Length..].Trim()
                : authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? authorization["Bearer ".Length..].Trim()
                    : "";
            var proof = context.Request.Headers[DeviceProof.HeaderName].ToString();
            var replay = context.RequestServices.GetRequiredService<DeviceProofReplayCache>();
            var error = DeviceProof.Validate(
                proof,
                thumbprint,
                context.Request.Method,
                Microsoft.AspNetCore.Http.Extensions.UriHelper.GetEncodedUrl(context.Request),
                token,
                replay,
                DateTimeOffset.UtcNow);
            if (error is not null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error });
                return;
            }

            await next();
        });
}
