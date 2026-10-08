using System.Net.Http;

namespace Snekl.Tests.Shared;

/// <summary>
/// Routes requests for one host to a TestServer handler that can be assigned after the factory starts.
/// </summary>
public sealed class TestServerRoutingHandler : DelegatingHandler
{
    private readonly string _authority;

    public TestServerRoutingHandler(string baseUrl, HttpMessageHandler? inner = null)
    {
        _authority = new Uri(baseUrl.TrimEnd('/') + "/").GetLeftPart(UriPartial.Authority);
        InnerHandler = inner ?? new HttpClientHandler();
    }

    public HttpMessageHandler? Target { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Target is not null &&
            request.RequestUri is not null &&
            string.Equals(
                request.RequestUri.GetLeftPart(UriPartial.Authority),
                _authority,
                StringComparison.OrdinalIgnoreCase))
        {
            var invoker = new HttpMessageInvoker(Target, disposeHandler: false);
            return invoker.SendAsync(request, cancellationToken);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
