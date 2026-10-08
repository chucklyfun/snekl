using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;

namespace Snekl.Infrastructure.Services;

public interface IReplyAlertPushSender
{
    /// <summary>Sends one collapsed alert per device. Returns tokens the provider rejected as unregistered.</summary>
    Task<IReadOnlyList<string>> SendAsync(
        IReadOnlyList<PushDevice> devices,
        ReplyAlertPush push,
        CancellationToken ct = default);
}

public sealed class NullReplyAlertPushSender : IReplyAlertPushSender
{
    public Task<IReadOnlyList<string>> SendAsync(
        IReadOnlyList<PushDevice> devices,
        ReplyAlertPush push,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}

/// <summary>
/// FCM legacy HTTP. Android replaces an existing notice when <c>notification.tag</c> matches the burst id.
/// No-ops when <c>Push:FcmServerKey</c> is empty.
/// </summary>
public sealed class FcmReplyAlertPushSender : IReplyAlertPushSender
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<FcmReplyAlertPushSender> _logger;

    public FcmReplyAlertPushSender(
        IHttpClientFactory http,
        IConfiguration config,
        ILogger<FcmReplyAlertPushSender> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> SendAsync(
        IReadOnlyList<PushDevice> devices,
        ReplyAlertPush push,
        CancellationToken ct = default)
    {
        var key = _config["Push:FcmServerKey"];
        if (string.IsNullOrWhiteSpace(key) || devices.Count == 0)
            return [];

        var tag = push.AlertId.ToString("N");
        var payload = JsonSerializer.Serialize(new
        {
            collapse_key = tag,
            priority = "high",
            notification = new
            {
                title = "Snekl",
                body = push.Body,
                tag
            },
            data = new Dictionary<string, string>
            {
                ["alertId"] = push.AlertId.ToString(),
                ["rootPostId"] = push.RootPostId?.ToString() ?? "",
                ["serverDid"] = push.ServerDid,
                ["title"] = push.Title,
                ["kind"] = push.Kind,
                ["slug"] = push.Slug ?? "",
                ["parentSlug"] = push.ParentSlug ?? "",
                ["forumParentSlug"] = push.ForumParentSlug ?? "",
                ["replyCount"] = push.ReplyCount.ToString()
            }
        });

        var dropped = new List<string>();
        var client = _http.CreateClient("fcm");
        foreach (var device in devices)
        {
            if (!string.Equals(device.Platform, "android", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(device.Token))
                continue;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://fcm.googleapis.com/fcm/send");
                request.Headers.TryAddWithoutValidation("Authorization", "key=" + key.Trim());
                request.Content = new StringContent(WithToken(payload, device.Token), Encoding.UTF8, "application/json");

                using var response = await client.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (FcmLegacyResponse.IsUnregistered(body))
                {
                    dropped.Add(device.Token);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "FCM rejected a reply alert ({Status}): {Body}",
                        (int)response.StatusCode,
                        Trim(body));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "FCM reply alert send failed");
            }
        }

        return dropped;
    }

    private static string WithToken(string payloadWithoutTo, string token)
    {
        using var doc = JsonDocument.Parse(payloadWithoutTo);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("to", token);
            foreach (var property in doc.RootElement.EnumerateObject())
                property.WriteTo(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Trim(string body) =>
        body.Length <= 300 ? body : body[..300];
}

public static class FcmLegacyResponse
{
    public static bool IsUnregistered(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return false;

        return body.Contains("NotRegistered", StringComparison.OrdinalIgnoreCase)
            || body.Contains("InvalidRegistration", StringComparison.OrdinalIgnoreCase);
    }
}
