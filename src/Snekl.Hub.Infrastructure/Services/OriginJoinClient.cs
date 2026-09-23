using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Snekl.Hub.Infrastructure.Services;

public interface IOriginJoinClient
{
    Task<Guid> RequestInviteAsync(
        string originBaseUrl,
        string authorizationHeader,
        string hubBaseUrl,
        string hubName,
        CancellationToken ct = default);
}

public class OriginJoinClient : IOriginJoinClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;

    public OriginJoinClient(HttpClient http) => _http = http;

    public async Task<Guid> RequestInviteAsync(
        string originBaseUrl,
        string authorizationHeader,
        string hubBaseUrl,
        string hubName,
        CancellationToken ct = default)
    {
        var baseUrl = originBaseUrl.Trim().TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/federation/invites")
        {
            Content = JsonContent.Create(new { hubBaseUrl, hubName })
        };
        if (!string.IsNullOrWhiteSpace(authorizationHeader))
            request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Content server rejected the member login.");
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException(
                "That user does not have RequestFederation on the content server.");
        if (!response.IsSuccessStatusCode)
        {
            var parsed = TryReadError(body);
            throw new InvalidOperationException(
                parsed ?? (string.IsNullOrWhiteSpace(body)
                    ? "Content server refused the federation request."
                    : body));
        }

        var invite = JsonSerializer.Deserialize<OriginInviteResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Content server returned an empty invite.");
        return invite.Id;
    }

    private static string? TryReadError(string body)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<ErrorBody>(body, JsonOptions);
            return string.IsNullOrWhiteSpace(parsed?.Error) ? null : parsed.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record OriginInviteResponse(Guid Id);
    private sealed record ErrorBody(string? Error);
}
