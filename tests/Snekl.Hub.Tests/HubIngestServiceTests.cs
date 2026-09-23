using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Snekl.Domain.Dtos;
using Snekl.Domain.Identity.Did;
using Snekl.Domain.Security;
using Snekl.Hub.Domain.Dtos;
using Snekl.Hub.Infrastructure.Services;

namespace Snekl.Hub.Tests;

public class HubIngestServiceTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task SignedCard_IsIngested()
    {
        var keys = DidKey.Generate();
        var verificationKey = DidKey.PublicKeyMultibase(keys.Did);
        var feed = new CapturingFeed();
        var ingest = CreateIngest(feed, verificationKey, requireSignatures: true);

        var card = SampleCard("http://origin.test");
        var timestamp = DateTime.UtcNow;
        var sourceId = Guid.NewGuid();
        var payload = JsonSerializer.SerializeToElement(card, Json);
        var signature = FederationEnvelopeSigner.Sign(
            keys.PrivateKey,
            HubIngestTypes.Card,
            sourceId,
            "http://origin.test/",
            timestamp,
            payload);
        var envelope = new FederationEnvelope(
            HubIngestTypes.Card,
            sourceId,
            "http://origin.test",
            timestamp,
            payload,
            signature);

        await ingest.AcceptCardAsync(JsonSerializer.SerializeToElement(envelope, Json));
        Assert.NotNull(feed.LastCard);
        Assert.Equal(card.PostId, feed.LastCard!.PostId);
    }

    [Fact]
    public async Task UnsignedCard_RejectedWhenSignaturesRequired()
    {
        var ingest = CreateIngest(new CapturingFeed(), "zignored", requireSignatures: true);
        var body = JsonSerializer.SerializeToElement(SampleCard("http://origin.test"), Json);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ingest.AcceptCardAsync(body));
        Assert.Contains("Signed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TamperedEnvelope_IsRejected()
    {
        var keys = DidKey.Generate();
        var verificationKey = DidKey.PublicKeyMultibase(keys.Did);
        var ingest = CreateIngest(new CapturingFeed(), verificationKey, requireSignatures: true);
        var card = SampleCard("http://origin.test");
        var timestamp = DateTime.UtcNow;
        var sourceId = Guid.NewGuid();
        var payload = JsonSerializer.SerializeToElement(card, Json);
        var signature = FederationEnvelopeSigner.Sign(
            keys.PrivateKey,
            HubIngestTypes.Card,
            sourceId,
            "http://origin.test",
            timestamp,
            payload);
        var tampered = card with { Title = "nope" };
        var envelope = new FederationEnvelope(
            HubIngestTypes.Card,
            sourceId,
            "http://origin.test",
            timestamp,
            JsonSerializer.SerializeToElement(tampered, Json),
            signature);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ingest.AcceptCardAsync(JsonSerializer.SerializeToElement(envelope, Json)));
    }

    private static HubIngestService CreateIngest(CapturingFeed feed, string key, bool requireSignatures)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hub:RequireSignatures"] = requireSignatures ? "true" : "false"
            })
            .Build();
        return new HubIngestService(
            feed,
            new StaticRegistry(),
            new StaticKeys(key),
            config);
    }

    private static HubFeedCardDto SampleCard(string origin) =>
        new(
            1,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "garden",
            "Garden",
            "did:web:origin.test",
            origin,
            $"{origin}/f/garden/1",
            "Hello",
            "Excerpt",
            null,
            "did:key:alice",
            "Alice",
            DateTime.UtcNow,
            null,
            "public",
            1,
            0,
            1,
            "upsert",
            DateTime.UtcNow);

    private sealed class StaticKeys(string key) : IOriginVerificationKeyResolver
    {
        public Task<string?> ResolveAsync(string did, string baseUrl, CancellationToken ct = default) =>
            Task.FromResult<string?>(key);
    }

    private sealed class StaticRegistry : IFederationRegistry
    {
        public Task<long> GetMapVersionAsync(CancellationToken ct = default) => Task.FromResult(1L);
        public Task<FederationMapDto> GetMapAsync(CancellationToken ct = default) =>
            Task.FromResult(new FederationMapDto(1, []));
        public Task<IReadOnlyList<RegisteredServerDto>> ListServersAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RegisteredServerDto>>([
                new("did:web:origin.test", "http://origin.test", "Origin", DateTime.UtcNow, 1)
            ]);
        public Task<IReadOnlyList<RegisteredServerDto>> ListJoinRequestsAsync(string? serverDid = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RegisteredServerDto>>([]);
        public Task<RegisteredServerDto> RegisterServerAsync(RegisterServerRequest request, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<RegisteredServerDto> AcceptJoinAsync(AcceptJoinRequest request, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task DeclineJoinAsync(DeclineJoinRequest request, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task AckMapAsync(MapAckRequest request, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task UnregisterServerAsync(string serverDid, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class CapturingFeed : IHubFeedService
    {
        public HubFeedCardDto? LastCard { get; private set; }

        public Task<IReadOnlyList<HubFeedCardDto>> GetFeedAsync(
            string sort = "hot", int limit = 50, string? viewerDid = null, int offset = 0, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<HubFeedCardDto>>([]);

        public Task<IReadOnlyList<HubSearchHitDto>> SearchAsync(
            string? query, int limit = 20, string? viewerDid = null, int offset = 0, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<HubSearchHitDto>>([]);

        public Task IngestCardAsync(HubFeedCardDto card, CancellationToken ct = default)
        {
            LastCard = card;
            return Task.CompletedTask;
        }

        public Task IngestScoresAsync(IReadOnlyList<HubScorePatchDto> patches, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task IngestMembershipAsync(HubForumMembershipDto payload, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ReindexAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
