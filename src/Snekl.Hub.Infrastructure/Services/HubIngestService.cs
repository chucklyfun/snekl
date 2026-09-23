using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Snekl.Domain.Dtos;
using Snekl.Domain.Security;
using Snekl.Hub.Domain.Dtos;

namespace Snekl.Hub.Infrastructure.Services;

public interface IHubIngestService
{
    Task AcceptCardAsync(JsonElement body, CancellationToken ct = default);
    Task AcceptScoresAsync(JsonElement body, CancellationToken ct = default);
    Task AcceptMembershipAsync(JsonElement body, CancellationToken ct = default);
    Task AcceptMembershipSyncAsync(JsonElement body, CancellationToken ct = default);
}

public sealed class HubIngestService : IHubIngestService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IHubFeedService _feed;
    private readonly IFederationRegistry _registry;
    private readonly IOriginVerificationKeyResolver _keys;
    private readonly bool _requireSignatures;

    public HubIngestService(
        IHubFeedService feed,
        IFederationRegistry registry,
        IOriginVerificationKeyResolver keys,
        IConfiguration config)
    {
        _feed = feed;
        _registry = registry;
        _keys = keys;
        _requireSignatures = config.GetValue("Hub:RequireSignatures", true);
    }

    public async Task AcceptCardAsync(JsonElement body, CancellationToken ct = default)
    {
        if (IsEnvelope(body))
        {
            var payload = await OpenEnvelopeAsync(body, HubIngestTypes.Card, ct);
            var card = payload.Deserialize<HubFeedCardDto>(JsonOptions)
                ?? throw new InvalidOperationException("Hub card payload is required.");
            await _feed.IngestCardAsync(card, ct);
            return;
        }

        RequireUnsignedAllowed();
        var raw = body.Deserialize<HubFeedCardDto>(JsonOptions)
            ?? throw new InvalidOperationException("Hub card body is required.");
        await _feed.IngestCardAsync(raw, ct);
    }

    public async Task AcceptScoresAsync(JsonElement body, CancellationToken ct = default)
    {
        if (IsEnvelope(body))
        {
            var payload = await OpenEnvelopeAsync(body, HubIngestTypes.Scores, ct);
            var patches = payload.Deserialize<List<HubScorePatchDto>>(JsonOptions) ?? [];
            await _feed.IngestScoresAsync(patches, ct);
            return;
        }

        RequireUnsignedAllowed();
        var raw = body.ValueKind == JsonValueKind.Array
            ? body.Deserialize<List<HubScorePatchDto>>(JsonOptions) ?? []
            : [];
        await _feed.IngestScoresAsync(raw, ct);
    }

    public async Task AcceptMembershipAsync(JsonElement body, CancellationToken ct = default)
    {
        if (IsEnvelope(body))
        {
            var payload = await OpenEnvelopeAsync(body, HubIngestTypes.Membership, ct);
            var membership = payload.Deserialize<HubForumMembershipDto>(JsonOptions)
                ?? throw new InvalidOperationException("Hub membership payload is required.");
            await _feed.IngestMembershipAsync(membership, ct);
            return;
        }

        RequireUnsignedAllowed();
        var raw = body.Deserialize<HubForumMembershipDto>(JsonOptions)
            ?? throw new InvalidOperationException("Hub membership body is required.");
        await _feed.IngestMembershipAsync(raw, ct);
    }

    public async Task AcceptMembershipSyncAsync(JsonElement body, CancellationToken ct = default)
    {
        IReadOnlyList<HubForumMembershipDto> rows;
        if (IsEnvelope(body))
        {
            var payload = await OpenEnvelopeAsync(body, HubIngestTypes.MembershipSync, ct);
            rows = payload.Deserialize<List<HubForumMembershipDto>>(JsonOptions) ?? [];
        }
        else
        {
            RequireUnsignedAllowed();
            rows = body.ValueKind == JsonValueKind.Array
                ? body.Deserialize<List<HubForumMembershipDto>>(JsonOptions) ?? []
                : [];
        }

        foreach (var row in rows)
            await _feed.IngestMembershipAsync(row, ct);
    }

    private async Task<JsonElement> OpenEnvelopeAsync(JsonElement body, string expectedType, CancellationToken ct)
    {
        var envelope = body.Deserialize<FederationEnvelope>(JsonOptions)
            ?? throw new InvalidOperationException("Federation envelope is required.");
        if (!string.Equals(envelope.Type, expectedType, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected envelope type {expectedType}.");
        if (!FederationEnvelopeSigner.IsTimestampValid(envelope.Timestamp, DateTime.UtcNow))
            throw new InvalidOperationException("Federation envelope timestamp is outside the allowed window.");

        var sourceUrl = (envelope.SourceNodeUrl ?? "").Trim().TrimEnd('/');
        var servers = await _registry.ListServersAsync(ct);
        var server = servers.FirstOrDefault(s =>
            string.Equals(s.BaseUrl.Trim().TrimEnd('/'), sourceUrl, StringComparison.OrdinalIgnoreCase));
        if (server is null)
            throw new InvalidOperationException("Envelope source is not an active federation member.");

        if (string.IsNullOrWhiteSpace(envelope.Signature))
        {
            if (_requireSignatures)
                throw new InvalidOperationException("Federation envelope signature is required.");
            return PayloadElement(envelope.Payload);
        }

        var key = await _keys.ResolveAsync(server.ServerDid, server.BaseUrl, ct);
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Unable to resolve origin verification key.");

        var payload = PayloadElement(envelope.Payload);
        var valid = FederationEnvelopeSigner.Verify(
            key,
            envelope.Type,
            envelope.SourceNodeId,
            sourceUrl,
            envelope.Timestamp,
            payload,
            envelope.Signature);
        if (!valid)
            throw new InvalidOperationException("Invalid federation envelope signature.");

        return payload;
    }

    private void RequireUnsignedAllowed()
    {
        if (_requireSignatures)
            throw new InvalidOperationException("Signed hub ingest envelope is required.");
    }

    private static bool IsEnvelope(JsonElement body) =>
        body.ValueKind == JsonValueKind.Object &&
        body.TryGetProperty("type", out _) &&
        body.TryGetProperty("payload", out _);

    private static JsonElement PayloadElement(object? payload)
    {
        if (payload is JsonElement element) return element;
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
