using System.Net.Http.Json;
using System.Text.Json;
using Snekl.Domain.Identity.Did;

namespace Snekl.Hub.Infrastructure.Services;

public interface IOriginVerificationKeyResolver
{
    Task<string?> ResolveAsync(string did, string baseUrl, CancellationToken ct = default);
}

public sealed class OriginVerificationKeyResolver : IOriginVerificationKeyResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;

    public OriginVerificationKeyResolver(HttpClient http) => _http = http;

    public async Task<string?> ResolveAsync(string did, string baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(did) || string.IsNullOrWhiteSpace(baseUrl)) return null;
        try
        {
            var url = DidWeb.DocumentUrl(baseUrl);
            var doc = await _http.GetFromJsonAsync<DidDocument>(url, JsonOptions, ct);
            return doc?.VerificationMethod.FirstOrDefault()?.PublicKeyMultibase;
        }
        catch
        {
            return null;
        }
    }
}
