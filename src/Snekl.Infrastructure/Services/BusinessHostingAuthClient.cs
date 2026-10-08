using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Security;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

/// <summary>Result of a signed call to Auth. <see cref="StatusCode"/> is 0 when Auth could not be reached.</summary>
public sealed record BusinessHostingCallResult<T>(int StatusCode, T? Value, string? Error = null, string? Code = null)
    where T : class
{
    public bool IsSuccess => StatusCode is >= 200 and < 300 && Value is not null;
}

public sealed record BusinessServerLink(
    string MemberDid,
    Guid BusinessAccountId,
    Guid ServerId,
    string ServerDid,
    string ServerBaseUrl);

/// <summary>
/// Content -> Auth business hosting calls, signed as this content host. Auth must list the host's DID
/// and base URL under <c>BusinessHosting:TrustedContentServers</c>.
/// </summary>
public interface IBusinessHostingAuthClient
{
    Task<BusinessHostingCallResult<BusinessHostingStatus>> GetStatusAsync(
        string issuerUrl,
        string memberDid,
        Guid? businessAccountId,
        CancellationToken ct = default);

    Task<BusinessHostingCallResult<BusinessHostingLinkResult>> LinkAsync(
        string issuerUrl,
        BusinessServerLink link,
        CancellationToken ct = default);
}

public sealed class BusinessHostingAuthClient : IBusinessHostingAuthClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly SneklDbContext _db;

    public BusinessHostingAuthClient(HttpClient http, SneklDbContext db)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(10);
        _db = db;
    }

    public async Task<BusinessHostingCallResult<BusinessHostingStatus>> GetStatusAsync(
        string issuerUrl,
        string memberDid,
        Guid? businessAccountId,
        CancellationToken ct = default)
    {
        var (host, key) = await LoadHostIdentityAsync(ct);
        var audience = BusinessHostingEnvelope.NormalizeUrl(issuerUrl);
        var payload = new BusinessHostingStatusQuery(
            host.Did,
            audience,
            BusinessHostingEnvelope.NewNonce(),
            memberDid,
            businessAccountId);
        var envelope = BusinessHostingEnvelope.Seal(key, BusinessHostingEnvelopeTypes.Status, host.Id, host.BaseUrl, payload);
        return await PostAsync<BusinessHostingStatus>($"{audience}/api/business/hosting/status", envelope, ct);
    }

    public async Task<BusinessHostingCallResult<BusinessHostingLinkResult>> LinkAsync(
        string issuerUrl,
        BusinessServerLink link,
        CancellationToken ct = default)
    {
        var (host, key) = await LoadHostIdentityAsync(ct);
        var audience = BusinessHostingEnvelope.NormalizeUrl(issuerUrl);
        var payload = new BusinessHostingLinkCommand(
            host.Did,
            audience,
            BusinessHostingEnvelope.NewNonce(),
            link.MemberDid,
            link.BusinessAccountId,
            link.ServerId,
            link.ServerDid,
            BusinessHostingEnvelope.NormalizeUrl(link.ServerBaseUrl));
        var envelope = BusinessHostingEnvelope.Seal(key, BusinessHostingEnvelopeTypes.Link, host.Id, host.BaseUrl, payload);
        return await PostAsync<BusinessHostingLinkResult>($"{audience}/api/business/hosting/link", envelope, ct);
    }

    /// <summary>
    /// The host's own identity: the first <see cref="NodeConfig"/> (the one <c>NodeService</c> creates,
    /// usually a did:web of <c>Node:BaseUrl</c>). Virtual servers spawned later never sign these calls.
    /// </summary>
    private async Task<(NodeConfig Host, byte[] PrivateKey)> LoadHostIdentityAsync(CancellationToken ct)
    {
        var host = await _db.NodeConfig.AsNoTracking().OrderBy(n => n.CreatedAt).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("This content host has no server identity yet.");
        if (string.IsNullOrWhiteSpace(host.VerificationPrivateKeyPkcs8) || string.IsNullOrWhiteSpace(host.Did))
            throw new InvalidOperationException("This content host has no signing key yet.");
        return (host, Convert.FromBase64String(host.VerificationPrivateKeyPkcs8));
    }

    private async Task<BusinessHostingCallResult<T>> PostAsync<T>(string url, FederationEnvelope envelope, CancellationToken ct)
        where T : class
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(url, envelope, JsonOptions, ct);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
                return value is null
                    ? new BusinessHostingCallResult<T>(status, null, "The auth server returned an empty response.", "empty")
                    : new BusinessHostingCallResult<T>(status, value);
            }

            string? error = null;
            string? code = null;
            try
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                        error = e.GetString();
                    if (doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String)
                        code = c.GetString();
                }
            }
            catch (JsonException)
            {
                // Non-JSON error body; fall back to the status code.
            }

            return new BusinessHostingCallResult<T>(status, null, error ?? $"Auth server returned HTTP {status}.", code);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return new BusinessHostingCallResult<T>(0, null, "Could not reach the auth server.", "unreachable");
        }
    }
}
