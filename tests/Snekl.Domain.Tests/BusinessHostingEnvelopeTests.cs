using System.Text.Json;
using Snekl.Domain.Dtos;
using Snekl.Domain.Identity.Did;
using Snekl.Domain.Security;

namespace Snekl.Domain.Tests;

/// <summary>
/// The business hosting envelope survives the wire: Content signs a typed payload, Auth verifies the
/// JSON it received. Guards against serializer drift between the two sides.
/// </summary>
public class BusinessHostingEnvelopeTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static (FederationEnvelope Received, JsonElement Payload) RoundTrip(FederationEnvelope sent)
    {
        var json = JsonSerializer.Serialize(sent, WebJson);
        var received = JsonSerializer.Deserialize<FederationEnvelope>(json, WebJson)!;
        return (received, (JsonElement)received.Payload);
    }

    [Fact]
    public void Seal_VerifiesAfterJsonRoundTrip()
    {
        var keys = DidKey.Generate();
        var nodeId = Guid.NewGuid();
        var payload = new BusinessHostingLinkCommand(
            "did:web:content.test", "http://auth.test", BusinessHostingEnvelope.NewNonce(), "did:key:zMember",
            Guid.NewGuid(), Guid.NewGuid(), "did:key:zServer", "http://content.test");

        var sent = BusinessHostingEnvelope.Seal(keys.PrivateKey, BusinessHostingEnvelopeTypes.Link, nodeId, "http://content.test/", payload);
        Assert.Equal("http://content.test", sent.SourceNodeUrl);

        var (received, element) = RoundTrip(sent);
        Assert.True(FederationEnvelopeSigner.Verify(
            DidKey.PublicKeyMultibase(keys.Did),
            received.Type,
            received.SourceNodeId,
            received.SourceNodeUrl,
            received.Timestamp,
            element,
            received.Signature!));
    }

    [Fact]
    public void Seal_NullOptionalField_StillVerifies()
    {
        var keys = DidKey.Generate();
        var payload = new BusinessHostingStatusQuery("did:web:content.test", "http://auth.test", "n1", "did:key:zMember");
        var sent = BusinessHostingEnvelope.Seal(keys.PrivateKey, BusinessHostingEnvelopeTypes.Status, Guid.NewGuid(), "http://content.test", payload);
        var (received, element) = RoundTrip(sent);
        Assert.True(FederationEnvelopeSigner.Verify(
            DidKey.PublicKeyMultibase(keys.Did), received.Type, received.SourceNodeId, received.SourceNodeUrl,
            received.Timestamp, element, received.Signature!));
    }

    [Fact]
    public void TamperedPayloadOrWrongKey_FailsVerification()
    {
        var keys = DidKey.Generate();
        var payload = new BusinessHostingStatusQuery("did:web:content.test", "http://auth.test", "n2", "did:key:zMember");
        var sent = BusinessHostingEnvelope.Seal(keys.PrivateKey, BusinessHostingEnvelopeTypes.Status, Guid.NewGuid(), "http://content.test", payload);

        var json = JsonSerializer.Serialize(sent, WebJson).Replace("did:key:zMember", "did:key:zSomeoneElse");
        var tampered = JsonSerializer.Deserialize<FederationEnvelope>(json, WebJson)!;
        Assert.False(FederationEnvelopeSigner.Verify(
            DidKey.PublicKeyMultibase(keys.Did), tampered.Type, tampered.SourceNodeId, tampered.SourceNodeUrl,
            tampered.Timestamp, (JsonElement)tampered.Payload, tampered.Signature!));

        var (received, element) = RoundTrip(sent);
        Assert.False(FederationEnvelopeSigner.Verify(
            DidKey.PublicKeyMultibase(DidKey.Generate().Did), received.Type, received.SourceNodeId, received.SourceNodeUrl,
            received.Timestamp, element, received.Signature!));
    }
}
