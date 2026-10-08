using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snekl.Domain.Dtos;
using Snekl.Domain.Identity.Did;
using Snekl.Domain.Security;
using Snekl.Redis;

namespace Snekl.Account.Infrastructure.Security;

/// <summary>
/// <c>BusinessHosting</c> section of Auth appsettings: which content servers may ask about business
/// approval and report hosted servers. Empty by default, so nothing is trusted until configured.
/// </summary>
public sealed class BusinessHostingOptions
{
    public const string SectionName = "BusinessHosting";

    public List<TrustedContentServerOptions> TrustedContentServers { get; set; } = [];
}

/// <summary>
/// One trusted content host. <see cref="Did"/> and <see cref="BaseUrl"/> must match the signed
/// envelope. The verification key comes from <see cref="VerificationKeyMultibase"/> when set (pinned),
/// else from the DID itself (did:key), else from <c>{BaseUrl}/.well-known/did.json</c> (did:web).
/// </summary>
public sealed class TrustedContentServerOptions
{
    public string Name { get; set; } = string.Empty;
    public string Did { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? VerificationKeyMultibase { get; set; }
}

public enum BusinessHostingRejection
{
    None,
    /// <summary>No envelope or no signature.</summary>
    Missing,
    WrongType,
    /// <summary>Timestamp outside <see cref="FederationEnvelopeSigner.MaxEnvelopeAge"/> / clock skew.</summary>
    Stale,
    Malformed,
    UntrustedSigner,
    KeyUnavailable,
    BadSignature,
    WrongAudience,
    Replayed
}

public sealed record BusinessHostingVerification<T>(
    T? Payload,
    TrustedContentServerOptions? Signer,
    BusinessHostingRejection Rejection,
    string? Error)
    where T : class
{
    public bool IsValid => Rejection == BusinessHostingRejection.None && Payload is not null;
}

public interface IBusinessHostingRequestVerifier
{
    Task<BusinessHostingVerification<T>> VerifyAsync<T>(
        FederationEnvelope? envelope,
        string expectedType,
        CancellationToken ct = default)
        where T : class, IBusinessHostingPayload;
}

/// <summary>
/// Single-use nonces for signed business hosting requests. Uses Redis when configured (shared across
/// Auth instances), otherwise an in-process cache. Entries live as long as an envelope can be fresh.
/// </summary>
public sealed class BusinessHostingReplayGuard
{
    public static readonly TimeSpan Window =
        FederationEnvelopeSigner.MaxEnvelopeAge + FederationEnvelopeSigner.MaxClockSkew;

    private readonly DeviceProofReplayCache _local = new();
    private readonly IRedisDedupService? _redis;

    public BusinessHostingReplayGuard(IServiceProvider services) =>
        _redis = services.GetService(typeof(IRedisDedupService)) as IRedisDedupService;

    public async Task<bool> TryRememberAsync(string key, CancellationToken ct = default)
    {
        if (_redis is not null)
            return await _redis.TryMarkProcessedAsync($"business-hosting:{key}", Window, ct);
        return _local.TryRemember(key, DateTimeOffset.UtcNow.Add(Window));
    }
}

/// <summary>
/// Verifies a <see cref="FederationEnvelope"/> from a trusted content server. Checks, in order:
/// signature present, envelope type, timestamp window, payload shape, trusted signer (DID + base URL),
/// Ed25519 signature, audience (this Auth's base URL), and nonce replay. The nonce is only burned
/// after the signature checks out, so unsigned junk cannot use up a real request's nonce.
/// </summary>
public sealed class BusinessHostingRequestVerifier : IBusinessHostingRequestVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<BusinessHostingOptions> _options;
    private readonly BusinessHostingReplayGuard _replay;
    private readonly ILogger<BusinessHostingRequestVerifier> _logger;
    private readonly string _authBaseUrl;

    public BusinessHostingRequestVerifier(
        HttpClient http,
        IOptionsMonitor<BusinessHostingOptions> options,
        IConfiguration config,
        BusinessHostingReplayGuard replay,
        ILogger<BusinessHostingRequestVerifier> logger)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(5);
        _options = options;
        _replay = replay;
        _logger = logger;
        _authBaseUrl = BusinessHostingEnvelope.NormalizeUrl(config["Auth:BaseUrl"] ?? "http://localhost:5100");
    }

    public async Task<BusinessHostingVerification<T>> VerifyAsync<T>(
        FederationEnvelope? envelope,
        string expectedType,
        CancellationToken ct = default)
        where T : class, IBusinessHostingPayload
    {
        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Signature))
            return Reject<T>(BusinessHostingRejection.Missing, "A signed request is required.");

        if (!string.Equals(envelope.Type, expectedType, StringComparison.Ordinal))
            return Reject<T>(BusinessHostingRejection.WrongType, $"Expected envelope type {expectedType}.");

        if (!FederationEnvelopeSigner.IsTimestampValid(envelope.Timestamp, DateTime.UtcNow))
            return Reject<T>(BusinessHostingRejection.Stale, "The request timestamp is outside the allowed window.");

        JsonElement payloadElement;
        T? payload;
        try
        {
            payloadElement = ToElement(envelope.Payload);
            payload = payloadElement.ValueKind == JsonValueKind.Object
                ? payloadElement.Deserialize<T>(JsonOptions)
                : null;
        }
        catch (JsonException)
        {
            return Reject<T>(BusinessHostingRejection.Malformed, "The request payload is malformed.");
        }

        if (payload is null
            || string.IsNullOrWhiteSpace(payload.SignerDid)
            || string.IsNullOrWhiteSpace(payload.Audience)
            || string.IsNullOrWhiteSpace(payload.Nonce))
            return Reject<T>(BusinessHostingRejection.Malformed, "The request payload is missing required fields.");

        var sourceUrl = BusinessHostingEnvelope.NormalizeUrl(envelope.SourceNodeUrl);
        var signer = _options.CurrentValue.TrustedContentServers.FirstOrDefault(s =>
            !string.IsNullOrWhiteSpace(s.Did)
            && string.Equals(s.Did.Trim(), payload.SignerDid.Trim(), StringComparison.Ordinal)
            && string.Equals(BusinessHostingEnvelope.NormalizeUrl(s.BaseUrl), sourceUrl, StringComparison.OrdinalIgnoreCase));
        if (signer is null)
            return Reject<T>(
                BusinessHostingRejection.UntrustedSigner,
                "The signing content server is not trusted by this auth server.");

        var key = await ResolveKeyAsync(signer, envelope.SourceNodeId, ct);
        if (string.IsNullOrWhiteSpace(key))
            return Reject<T>(BusinessHostingRejection.KeyUnavailable, "Unable to resolve the content server's verification key.");

        bool valid;
        try
        {
            valid = FederationEnvelopeSigner.Verify(
                key,
                envelope.Type,
                envelope.SourceNodeId,
                sourceUrl,
                envelope.Timestamp,
                payloadElement,
                envelope.Signature);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            valid = false;
        }

        if (!valid)
            return Reject<T>(BusinessHostingRejection.BadSignature, "Invalid request signature.");

        if (!string.Equals(BusinessHostingEnvelope.NormalizeUrl(payload.Audience), _authBaseUrl, StringComparison.OrdinalIgnoreCase))
            return Reject<T>(BusinessHostingRejection.WrongAudience, "The request was signed for a different auth server.");

        if (!await _replay.TryRememberAsync($"{signer.Did.Trim()}|{payload.Nonce.Trim()}", ct))
            return Reject<T>(BusinessHostingRejection.Replayed, "This request was already used.");

        return new BusinessHostingVerification<T>(payload, signer, BusinessHostingRejection.None, null);
    }

    private async Task<string?> ResolveKeyAsync(TrustedContentServerOptions signer, Guid sourceNodeId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(signer.VerificationKeyMultibase))
            return signer.VerificationKeyMultibase.Trim();

        var did = signer.Did.Trim();
        if (did.StartsWith(DidKey.Prefix, StringComparison.Ordinal))
        {
            try
            {
                return DidKey.PublicKeyMultibase(did);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
            {
                return null;
            }
        }

        if (!did.StartsWith(DidWeb.Prefix, StringComparison.Ordinal))
            return null;

        try
        {
            // did:web is only accepted for the configured base URL, so the document host is the DID host.
            if (!string.Equals(DidWeb.FromBaseUrl(signer.BaseUrl), did, StringComparison.Ordinal))
            {
                _logger.LogWarning("Business hosting signer {Did} does not match its base URL {BaseUrl}", did, signer.BaseUrl);
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, DidWeb.DocumentUrl(signer.BaseUrl));
            // A content host with several virtual servers needs to know which server's document we want.
            request.Headers.TryAddWithoutValidation("X-Snekl-Server", sourceNodeId.ToString());
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;
            var doc = await response.Content.ReadFromJsonAsync<DidDocument>(JsonOptions, ct);
            if (doc is null || !string.Equals(doc.Id, did, StringComparison.Ordinal))
                return null;
            return doc.VerificationMethod.FirstOrDefault()?.PublicKeyMultibase;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Could not resolve did:web document for business hosting signer {Did}", did);
            return null;
        }
    }

    private static JsonElement ToElement(object? payload)
    {
        if (payload is JsonElement element) return element;
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static BusinessHostingVerification<T> Reject<T>(BusinessHostingRejection rejection, string error)
        where T : class =>
        new(null, null, rejection, error);
}
