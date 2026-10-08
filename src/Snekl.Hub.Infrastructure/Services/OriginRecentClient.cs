using System.Net.Http.Json;
using System.Text.Json;

namespace Snekl.Hub.Infrastructure.Services;

public interface IOriginRecentClient
{
    Task<IReadOnlyList<OriginRecentThread>> GetRecentAsync(
        string originBaseUrl,
        int limit,
        CancellationToken ct = default);
}

public class OriginRecentClient : IOriginRecentClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;

    public OriginRecentClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<OriginRecentThread>> GetRecentAsync(
        string originBaseUrl,
        int limit,
        CancellationToken ct = default)
    {
        var baseUrl = originBaseUrl.Trim().TrimEnd('/');
        var pageSize = Math.Clamp(limit, 1, 100);
        var url = $"{baseUrl}/api/posts/recent?sort=updated&page=1&pageSize={pageSize}";
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var threads = await response.Content.ReadFromJsonAsync<List<OriginRecentThread>>(JsonOptions, ct);
        return threads ?? [];
    }
}
