using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Snekl.Domain.Dtos;
using Snekl.Domain.Enums;
using Snekl.Domain.Ranking;

namespace Snekl.Infrastructure.Services;

public interface IPostAggregationService
{
    Task<IReadOnlyList<RecentThreadDto>> GetRecentFromServersAsync(
        IReadOnlyList<string> serverBaseUrls,
        int page,
        int pageSize,
        PostSort sort,
        string? authorizationHeader,
        CancellationToken ct = default);
}

public class PostAggregationService : IPostAggregationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly IPostService _posts;

    public PostAggregationService(HttpClient http, IPostService posts)
    {
        _http = http;
        _posts = posts;
    }

    public async Task<IReadOnlyList<RecentThreadDto>> GetRecentFromServersAsync(
        IReadOnlyList<string> serverBaseUrls,
        int page,
        int pageSize,
        PostSort sort,
        string? authorizationHeader,
        CancellationToken ct = default)
    {
        var normalized = serverBaseUrls
            .Select(NormalizeBaseUrl)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
            return await _posts.GetRecentThreadsAsync(page, pageSize, sort, null, ct);

        if (normalized.Count == 1)
            return await FetchRecentAsync(normalized[0], page, pageSize, sort, authorizationHeader, ct);

        var fetchSize = Math.Max(pageSize, page * pageSize);
        var results = await Task.WhenAll(
            normalized.Select(url => FetchRecentAsync(url, 1, fetchSize, sort, authorizationHeader, ct)));

        var merged = results.SelectMany(threads => threads).ToList();
        var ranked = sort == PostSort.New
            ? merged.OrderByDescending(t => t.Post.CreatedAt).ToList()
            : merged.OrderByDescending(t => PostRanking.HotScore(t.Post.UpvoteCount, t.Post.CreatedAt)).ToList();

        return ranked
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
    }

    private async Task<IReadOnlyList<RecentThreadDto>> FetchRecentAsync(
        string baseUrl,
        int page,
        int pageSize,
        PostSort sort,
        string? authorizationHeader,
        CancellationToken ct)
    {
        var sortParam = sort == PostSort.New ? "new" : "hot";
        var url = $"{baseUrl}/api/posts/recent?sort={sortParam}&page={page}&pageSize={pageSize}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(authorizationHeader))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorizationHeader);

        try
        {
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return [];

            var threads = await response.Content.ReadFromJsonAsync<List<RecentThreadDto>>(JsonOptions, ct);
            return threads ?? [];
        }
        catch
        {
            return [];
        }
    }

    internal static string NormalizeBaseUrl(string baseUrl)
    {
        baseUrl = baseUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            return baseUrl;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return baseUrl;

        if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) &&
            (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
        {
            return new UriBuilder(uri) { Scheme = "http" }.Uri.ToString().TrimEnd('/');
        }

        return baseUrl;
    }
}
