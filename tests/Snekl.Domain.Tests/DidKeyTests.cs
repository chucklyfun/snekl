using System.Text;
using Snekl.Domain.Identity.Did;

namespace Snekl.Domain.Tests;

public class DidKeyTests
{
    [Fact]
    public void Generate_ProducesParseableDidAndRoundTripsKeys()
    {
        var keys = DidKey.Generate();

        Assert.StartsWith("did:key:z", keys.Did);
        Assert.Equal(32, keys.PublicKey.Length);
        Assert.Equal(32, keys.PrivateKey.Length);
        Assert.True(DidKey.TryParse(keys.Did, out var parsed));
        Assert.Equal(keys.PublicKey, parsed);
        Assert.Equal(DidKey.CreateFromPublicKey(keys.PublicKey), keys.Did);
    }

    [Fact]
    public void SignAndVerify_SucceedsForMatchingKey()
    {
        var keys = DidKey.Generate();
        var data = Encoding.UTF8.GetBytes("hello snekl");
        var signature = DidKey.Sign(keys.PrivateKey, data);

        Assert.True(DidKey.Verify(keys.Did, data, signature));
        Assert.False(DidKey.Verify(keys.Did, Encoding.UTF8.GetBytes("tampered"), signature));
    }

    [Fact]
    public void VerifyMultibaseProof_AcceptsValidOwnershipProof()
    {
        var keys = DidKey.Generate();
        var payload = $"snekl-register:alice:{keys.Did}";
        var signature = DidKey.Sign(keys.PrivateKey, Encoding.UTF8.GetBytes(payload));
        var proof = "z" + Base58Encoding.Encode(signature);

        Assert.True(DidKey.VerifyMultibaseProof(keys.Did, payload, proof));
        Assert.False(DidKey.VerifyMultibaseProof(keys.Did, payload, "z" + Base58Encoding.Encode(new byte[64])));
    }

    [Theory]
    [InlineData("")]
    [InlineData("did:web:example.com")]
    [InlineData("did:key:notvalid")]
    [InlineData("did:key:xabc")]
    public void TryParse_RejectsInvalidDid(string did)
    {
        Assert.False(DidKey.TryParse(did, out _));
    }

    [Fact]
    public void PublicKeyMultibase_MatchesCreateFromPublicKeySuffix()
    {
        var keys = DidKey.Generate();
        var multibase = DidKey.PublicKeyMultibase(keys.Did);
        Assert.StartsWith("z", multibase);
        Assert.True(DidKey.TryDecodePublicKeyMultibase(multibase, out var decoded));
        Assert.Equal(keys.PublicKey, decoded);
    }

    [Fact]
    public void CreateFromPublicKey_RejectsWrongLength()
    {
        Assert.Throws<ArgumentException>(() => DidKey.CreateFromPublicKey(new byte[16]));
    }
}
