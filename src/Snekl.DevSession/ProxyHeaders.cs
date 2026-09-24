using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Snekl.DevSession;

public static class ProxyHeaders
{
    /// <summary>
    /// Trust X-Forwarded-* from the edge proxy (Caddy). Apps bind HTTP internally;
    /// public scheme/host come from the proxy.
    /// </summary>
    public static WebApplication UseSneklForwardedHeaders(this WebApplication app)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost
        };
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        app.UseForwardedHeaders(options);
        return app;
    }
}
