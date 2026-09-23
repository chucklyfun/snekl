using System.Net.Http.Headers;
using System.Net.Http.Json;
using Snekl.Domain.Dtos;

namespace Snekl.Account.Infrastructure.Services;

public interface IContentInstallLookup
{
    Task<ExtensionInstallDto?> GetAsync(
        string contentBaseUrl,
        Guid installId,
        string userAccessToken,
        CancellationToken ct = default);

    Task<RateLimitPolicyDto?> GetRateLimitingAsync(string contentBaseUrl, CancellationToken ct = default);
}

public class ContentInstallLookup : IContentInstallLookup
{
    private readonly IHttpClientFactory _http;

    public ContentInstallLookup(IHttpClientFactory http) => _http = http;

    public async Task<ExtensionInstallDto?> GetAsync(
        string contentBaseUrl,
        Guid installId,
        string userAccessToken,
        CancellationToken ct = default)
    {
        var client = _http.CreateClient("extension-install");
        var url = $"{contentBaseUrl.TrimEnd('/')}/api/extensions/installs/{installId:D}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userAccessToken);
        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<ExtensionInstallDto>(cancellationToken: ct);
    }

    public async Task<RateLimitPolicyDto?> GetRateLimitingAsync(string contentBaseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(contentBaseUrl))
            return null;

        var client = _http.CreateClient("extension-install");
        var url = $"{contentBaseUrl.TrimEnd('/')}/api/node";
        try
        {
            var node = await client.GetFromJsonAsync<NodeInfoDto>(url, cancellationToken: ct);
            return node?.RateLimiting;
        }
        catch
        {
            return null;
        }
    }
}
