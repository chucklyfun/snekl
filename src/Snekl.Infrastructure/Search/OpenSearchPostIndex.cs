using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Snekl.Infrastructure.Search;

public sealed class OpenSearchPostIndex : IPostSearchIndex
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Regex HighlightTag = new("</?em>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly ILogger<OpenSearchPostIndex> _logger;
    private readonly string _index;

    public OpenSearchPostIndex(
        HttpClient http,
        IOptions<OpenSearchOptions> options,
        ILogger<OpenSearchPostIndex> logger)
    {
        _http = http;
        _logger = logger;
        var name = options.Value.Index?.Trim();
        _index = string.IsNullOrWhiteSpace(name) ? "snekl-posts" : name.ToLowerInvariant();
    }

    public bool IsEnabled => true;

    public async Task EnsureIndexAsync(CancellationToken ct = default)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                await WaitForClusterAsync(ct);
                using var head = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, _index), ct);
                if (head.StatusCode == HttpStatusCode.OK) return;
                if (head.StatusCode != HttpStatusCode.NotFound)
                    await EnsureSuccess(head, "HEAD index");

                var mapping = OpenSearchQueries.IndexMapping().ToJsonString();
                using var put = new StringContent(mapping, Encoding.UTF8, "application/json");
                using var created = await _http.PutAsync(_index, put, ct);
                if (created.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created) return;
                var body = await created.Content.ReadAsStringAsync(ct);
                if (created.StatusCode == HttpStatusCode.BadRequest &&
                    body.Contains("resource_already_exists_exception", StringComparison.Ordinal))
                    return;
                created.EnsureSuccessStatusCode();
                return;
            }
            catch (Exception ex) when (attempt < 11)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

        throw new InvalidOperationException($"OpenSearch index '{_index}' is not ready.", last);
    }

    public async Task DeleteIndexAsync(CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync(_index, ct);
        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound) return;
        await EnsureSuccess(response, "DELETE index");
    }

    public Task IndexAsync(PostSearchDocument document, CancellationToken ct = default) =>
        IndexManyAsync([document], ct, refresh: true);

    public Task IndexManyAsync(IReadOnlyList<PostSearchDocument> documents, CancellationToken ct = default) =>
        IndexManyAsync(documents, ct, refresh: false);

    private async Task IndexManyAsync(
        IReadOnlyList<PostSearchDocument> documents,
        CancellationToken ct,
        bool refresh)
    {
        if (documents.Count == 0) return;

        var ndjson = new StringBuilder();
        foreach (var document in documents)
        {
            ndjson.Append("{\"index\":{\"_index\":")
                .Append(JsonSerializer.Serialize(_index))
                .Append(",\"_id\":")
                .Append(JsonSerializer.Serialize(document.PostId))
                .Append("}}\n");
            ndjson.Append(JsonSerializer.Serialize(document, Json)).Append('\n');
        }

        var path = refresh ? "_bulk?refresh=true" : "_bulk";
        using var content = new StringContent(ndjson.ToString(), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
        using var response = await _http.PostAsync(path, content, ct);
        await EnsureSuccess(response, "bulk index");
        var payload = await response.Content.ReadAsStringAsync(ct);
        using var parsed = JsonDocument.Parse(payload);
        if (parsed.RootElement.TryGetProperty("errors", out var errors) && errors.GetBoolean())
            _logger.LogWarning("OpenSearch bulk index reported errors for {Count} documents", documents.Count);
    }

    public async Task DeleteAsync(Guid postId, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync($"{_index}/_doc/{postId:D}?refresh=true", ct);
        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound) return;
        await EnsureSuccess(response, "DELETE doc");
    }

    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"{_index}/_count", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return 0;
        await EnsureSuccess(response, "count");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("count", out var count) ? count.GetInt64() : 0;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        using var response = await _http.PostAsync($"{_index}/_refresh", null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return;
        await EnsureSuccess(response, "refresh");
    }

    public async Task<IReadOnlyList<PostSearchMatch>> SearchAsync(
        string query,
        IReadOnlyList<Guid> forumIds,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (forumIds.Count == 0) return [];

        var from = Math.Max(0, (page - 1) * pageSize);
        var body = OpenSearchQueries.SearchBody(
            query,
            forumIds.Select(id => id.ToString("D")).ToList(),
            from,
            pageSize);
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"{_index}/_search", content, ct);
        await EnsureSuccess(response, "search");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return ParseHits(doc.RootElement);
    }

    private static IReadOnlyList<PostSearchMatch> ParseHits(JsonElement root)
    {
        if (!root.TryGetProperty("hits", out var hitsObj) ||
            !hitsObj.TryGetProperty("hits", out var hits) ||
            hits.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<PostSearchMatch>();
        foreach (var hit in hits.EnumerateArray())
        {
            var inner = BestInnerHit(hit);
            var source = inner ?? hit;
            if (!source.TryGetProperty("_source", out var src)) continue;
            if (!TryGetGuid(src, "threadRootId", out var threadRootId)) continue;
            TryGetGuid(src, "postId", out var postId);
            var parent = src.TryGetProperty("parentPostId", out var parentEl) &&
                         parentEl.ValueKind == JsonValueKind.String &&
                         !string.IsNullOrWhiteSpace(parentEl.GetString());
            var score = source.TryGetProperty("_score", out var scoreEl) && scoreEl.ValueKind == JsonValueKind.Number
                ? scoreEl.GetDouble()
                : 0;
            results.Add(new PostSearchMatch(
                threadRootId,
                postId,
                SnippetFrom(source, src),
                parent,
                score));
        }

        return results;
    }

    private static JsonElement? BestInnerHit(JsonElement hit)
    {
        if (!hit.TryGetProperty("inner_hits", out var inner) ||
            !inner.TryGetProperty("best", out var best) ||
            !best.TryGetProperty("hits", out var hitsObj) ||
            !hitsObj.TryGetProperty("hits", out var hits) ||
            hits.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var innerHit in hits.EnumerateArray())
            return innerHit;
        return null;
    }

    private static string SnippetFrom(JsonElement hit, JsonElement source)
    {
        if (hit.TryGetProperty("highlight", out var highlight))
        {
            if (FirstHighlight(highlight, "message") is { } message) return message;
            if (FirstHighlight(highlight, "title") is { } title) return title;
        }

        var titleText = source.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() ?? ""
            : "";
        var body = source.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString() ?? ""
            : "";
        var combined = string.IsNullOrWhiteSpace(titleText) ? body : $"{titleText}\n{body}";
        combined = combined.ReplaceLineEndings(" ").Trim();
        return combined.Length <= 180 ? combined : combined[..180] + "…";
    }

    private static string? FirstHighlight(JsonElement highlight, string field)
    {
        if (!highlight.TryGetProperty(field, out var fragments) || fragments.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var fragment in fragments.EnumerateArray())
        {
            if (fragment.ValueKind != JsonValueKind.String) continue;
            var text = HighlightTag.Replace(fragment.GetString() ?? "", "").Trim();
            if (text.Length > 0) return text;
        }

        return null;
    }

    private static bool TryGetGuid(JsonElement source, string name, out Guid id)
    {
        id = Guid.Empty;
        return source.TryGetProperty(name, out var el) &&
               el.ValueKind == JsonValueKind.String &&
               Guid.TryParse(el.GetString(), out id);
    }

    private async Task WaitForClusterAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("_cluster/health?wait_for_status=yellow&timeout=10s", ct);
        if (!response.IsSuccessStatusCode)
            _logger.LogDebug("OpenSearch cluster health returned {Status}", (int)response.StatusCode);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"OpenSearch {operation} failed ({(int)response.StatusCode}): {body}");
    }
}
