using System.Net.Http;

namespace Snekl.Tests.Shared;

/// <summary>
/// Routes HTTP calls for a specific auth base URL to an in-memory TestServer handler;
/// all other requests use the inner handler.
/// </summary>
public sealed class AuthJwksRoutingHandler : DelegatingHandler
{
    private readonly string _authAuthority;
    private readonly HttpMessageHandler _authServerHandler;

    public AuthJwksRoutingHandler(string authBaseUrl, HttpMessageHandler authServerHandler, HttpMessageHandler? inner = null)
    {
        _authAuthority = new Uri(authBaseUrl.TrimEnd('/') + "/").GetLeftPart(UriPartial.Authority);
        _authServerHandler = authServerHandler;
        InnerHandler = inner ?? new HttpClientHandler();
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not null &&
            string.Equals(
                request.RequestUri.GetLeftPart(UriPartial.Authority),
                _authAuthority,
                StringComparison.OrdinalIgnoreCase))
        {
            var invoker = new HttpMessageInvoker(_authServerHandler, disposeHandler: false);
            return invoker.SendAsync(request, cancellationToken);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
