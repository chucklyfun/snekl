using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Snekl.Billing;

public static partial class BillingRedactor
{
    const int MaxInput = 64_000;
    const int MaxBody = 16_000;

    static readonly HashSet<string> SecretFields =
    [
        "authorization", "cookie", "set-cookie", "stripe-signature", "password", "secret", "client_secret", "api_key", "apikey",
        "token", "access_token", "refresh_token", "code", "cvc", "cvv", "ssn", "account_number", "routing_number", "webhook_secret",
    ];

    static readonly HashSet<string> CardFields = ["number", "card_number", "pan", "cardnumber"];
    static readonly HashSet<string> MaskedHeaders = ["authorization", "proxy-authorization", "cookie", "set-cookie", "stripe-signature"];

    public static readonly string[] RequestHeaderNames = ["content-type", "idempotency-key", "stripe-version", "authorization", "stripe-signature"];
    public static readonly string[] ResponseHeaderNames = ["content-type", "request-id", "stripe-request-id", "stripe-version"];

    public static IReadOnlyDictionary<string, string> PickHeaders(IEnumerable<KeyValuePair<string, string>> headers, IReadOnlyList<string> names)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in headers) found[key] = value;
        var picked = new Dictionary<string, string>();
        foreach (var name in names)
        {
            if (!found.TryGetValue(name, out var value) || value.Length == 0) continue;
            picked[name] = MaskedHeaders.Contains(name) ? "***" : MaskString(value);
        }
        return picked;
    }

    public static string? MaskBody(string? body, string? contentType)
    {
        if (body is null) return null;
        var source = body.Length > MaxInput ? body[..MaxInput] : body;
        var type = contentType?.ToLowerInvariant() ?? "";
        var masked = type.Contains("application/json", StringComparison.Ordinal) ? MaskJsonBody(source)
            : type.Contains("application/x-www-form-urlencoded", StringComparison.Ordinal) ? MaskFormBody(source)
            : MaskString(source);
        return masked.Length <= MaxBody ? masked : $"{masked[..MaxBody]}…[truncated]";
    }

    public static string MaskString(string value) =>
        CardPattern().Replace(
            SecretPattern().Replace(
                WebhookSecretPattern().Replace(
                    BearerPattern().Replace(value, "Bearer ***"),
                    "whsec_***"),
                match => match.Value[..(match.Value.IndexOf('_', 3) + 1)] + "***"),
            match => MaskCard(match.Value));

    static string MaskJsonBody(string source)
    {
        try
        {
            var node = JsonNode.Parse(source);
            return MaskJson(node, "")?.ToJsonString() ?? "null";
        }
        catch (System.Text.Json.JsonException)
        {
            return MaskString(source);
        }
    }

    static string MaskFormBody(string source)
    {
        var parts = new List<string>();
        foreach (var part in source.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var rawKey = eq < 0 ? part : part[..eq];
            var rawValue = eq < 0 ? "" : Uri.UnescapeDataString(part[(eq + 1)..]);
            var key = Uri.UnescapeDataString(rawKey);
            parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(MaskField(key, rawValue))}");
        }
        return string.Join("&", parts);
    }

    static JsonNode? MaskJson(JsonNode? node, string key)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return MaskField(key, text);
        if (node is JsonArray array)
        {
            var masked = new JsonArray();
            foreach (var item in array) masked.Add(MaskJson(item?.DeepClone(), key));
            return masked;
        }
        if (node is JsonObject obj)
        {
            var masked = new JsonObject();
            foreach (var (childKey, child) in obj) masked[childKey] = MaskJson(child?.DeepClone(), childKey);
            return masked;
        }
        return node?.DeepClone();
    }

    static string MaskField(string key, string value)
    {
        var name = FieldName(key);
        if (CardFields.Contains(name)) return MaskCard(value);
        if (SecretFields.Contains(name) || name.Contains("secret", StringComparison.Ordinal) || name.Contains("password", StringComparison.Ordinal) || name.Contains("token", StringComparison.Ordinal))
            return "***";
        return MaskString(value);
    }

    static string FieldName(string key)
    {
        var parts = key.Split(['[', ']', '.'], StringSplitOptions.RemoveEmptyEntries);
        return (parts.Length == 0 ? key : parts[^1]).ToLowerInvariant();
    }

    static string MaskCard(string value)
    {
        var digits = DigitPattern().Replace(value, "");
        if (digits.Length < 13) return "***";
        return $"****{digits[^4..]}";
    }

    [GeneratedRegex(@"\bBearer\s+\S+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"\bwhsec_[A-Za-z0-9]+")]
    private static partial Regex WebhookSecretPattern();

    [GeneratedRegex(@"\b(?:sk|rk|pk)_(?:test|live)_[A-Za-z0-9]+")]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"\b(?:\d[ -]?){13,19}\b")]
    private static partial Regex CardPattern();

    [GeneratedRegex(@"\D")]
    private static partial Regex DigitPattern();
}
