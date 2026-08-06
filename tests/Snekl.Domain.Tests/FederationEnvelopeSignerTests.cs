using Snekl.Domain.Identity.Did;
using Snekl.Domain.Security;

namespace Snekl.Domain.Tests;

public class FederationEnvelopeSignerTests
{
    [Fact]
    public void SignAndVerify_RoundTrips()
    {
        var keys = DidKey.Generate();
        var verificationKey = DidKey.PublicKeyMultibase(keys.Did);
        var nodeId = Guid.NewGuid();
        var timestamp = DateTime.UtcNow;
        var payload = new { id = Guid.NewGuid(), name = "general" };

        var signature = FederationEnvelopeSigner.Sign(
            keys.PrivateKey,
            "forum.create",
            nodeId,
            "http://localhost:5001/",
            timestamp,
            payload);

        Assert.True(FederationEnvelopeSigner.Verify(
            verificationKey,
            "forum.create",
            nodeId,
            "http://localhost:5001",
            timestamp,
            payload,
            signature));
    }

    [Fact]
    public void Verify_FailsWhenPayloadTampered()
    {
        var keys = DidKey.Generate();
        var verificationKey = DidKey.PublicKeyMultibase(keys.Did);
        var nodeId = Guid.NewGuid();
        var timestamp = DateTime.UtcNow;

        var signature = FederationEnvelopeSigner.Sign(
            keys.PrivateKey,
            "post.create",
            nodeId,
            "http://node.example",
            timestamp,
            new { message = "hello" });

        Assert.False(FederationEnvelopeSigner.Verify(
            verificationKey,
            "post.create",
            nodeId,
            "http://node.example",
            timestamp,
            new { message = "tampered" },
            signature));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-2, true)]
    [InlineData(-6, false)]
    [InlineData(2, false)]
    public void IsTimestampValid_EnforcesAgeAndSkew(int minutesFromNow, bool expected)
    {
        var now = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
        var timestamp = now.AddMinutes(minutesFromNow);

        Assert.Equal(expected, FederationEnvelopeSigner.IsTimestampValid(timestamp, now));
    }
}
