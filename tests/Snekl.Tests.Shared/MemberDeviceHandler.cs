using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Snekl.Domain.Security;

namespace Snekl.Tests.Shared;

/// <summary>
/// Presents one P-256 key for every test client and signs member tokens that carry a thumbprint.
/// </summary>
public sealed class MemberDeviceHandler : DelegatingHandler
{
    public static readonly MemberDeviceKey Device = MemberDeviceKey.Create();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation(DeviceProof.DeviceKeyHeaderName, Device.PublicJwkJson);
        if (request.Headers.Authorization is { Scheme: "Bearer", Parameter: { } token }
            && DeviceProof.ReadThumbprint(token) is not null
            && request.RequestUri is not null)
        {
            await AddContentLookupProofAsync(request, token, cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", token);
            request.Headers.Remove(DeviceProof.HeaderName);
            request.Headers.TryAddWithoutValidation(
                DeviceProof.HeaderName,
                Device.CreateProof(request.Method.Method, request.RequestUri.AbsoluteUri, token));
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static async Task AddContentLookupProofAsync(HttpRequestMessage request, string token, CancellationToken ct)
    {
        if (request.Content is null || request.RequestUri is null
            || !request.RequestUri.AbsolutePath.EndsWith("/api/oauth/authorize", StringComparison.OrdinalIgnoreCase))
            return;

        var json = await request.Content.ReadAsStringAsync(ct);
        var mediaType = request.Content.Headers.ContentType?.MediaType ?? "application/json";
        request.Content = new StringContent(json, Encoding.UTF8, mediaType);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("contentBaseUrl", out var baseUrl) || baseUrl.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("installId", out var installId))
                return;
            var idText = installId.ValueKind == JsonValueKind.String ? installId.GetString() : installId.ToString();
            if (!Guid.TryParse(idText, out var id))
                return;

            var url = $"{baseUrl.GetString()!.TrimEnd('/')}/api/extensions/installs/{id:D}";
            request.Headers.TryAddWithoutValidation("X-Snekl-Content-DPoP", Device.CreateProof("GET", url, token));
        }
        catch (JsonException)
        {
        }
    }
}

public sealed class MemberDeviceKey
{
    private readonly ECDsa _key;
    public string X { get; }
    public string Y { get; }
    public string Thumbprint { get; }
    public string PublicJwkJson { get; }

    private MemberDeviceKey(ECDsa key, string x, string y)
    {
        _key = key;
        X = x;
        Y = y;
        Thumbprint = DeviceProof.Thumbprint(x, y);
        PublicJwkJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = x,
            ["y"] = y
        });
    }

    public static MemberDeviceKey Create()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(false);
        return new MemberDeviceKey(
            key,
            DeviceProof.Base64Url(parameters.Q.X!),
            DeviceProof.Base64Url(parameters.Q.Y!));
    }

    public string CreateProof(string method, string url, string accessToken) =>
        DeviceProof.Create(_key, X, Y, method, url, accessToken, DateTimeOffset.UtcNow);
}
