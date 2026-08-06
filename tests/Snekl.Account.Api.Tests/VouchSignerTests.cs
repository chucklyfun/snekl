using Snekl.Account.Domain.Dtos;
using Snekl.Account.Infrastructure.Security;

namespace Snekl.Account.Api.Tests;

public class VouchSignerTests
{
    [Fact]
    public void SignAndVerify_RoundTrips()
    {
        var payload = new { Hello = "world", N = 3 };
        const string key = "test-signing-key";
        var signature = VouchSigner.Sign(payload, key);

        Assert.True(VouchSigner.Verify(payload, signature, key));
        Assert.False(VouchSigner.Verify(payload, signature, "other-key"));
    }

    [Fact]
    public void VouchBuilder_CreateAndValidate()
    {
        const string key = "partner-key";
        var vouch = VouchBuilder.Create(new UnsignedVouch(
            Guid.NewGuid(),
            "did:key:zabc",
            Guid.NewGuid(),
            "http://localhost:5100",
            "alice",
            "Alice",
            "alice@example.com",
            "",
            null,
            DateTime.UtcNow.AddHours(1)), key);

        Assert.True(VouchBuilder.TryValidate(vouch, key));
        Assert.False(VouchBuilder.TryValidate(vouch with { ExpiresAt = DateTime.UtcNow.AddMinutes(-1) }, key));
    }

    [Theory]
    [InlineData("http://localhost:5100", "local")]
    [InlineData("https://auth-alpha.example.com", "alpha")]
    public void DeriveDefaultTag_FromAuthUrl(string url, string expected)
    {
        Assert.Equal(expected, VouchSigner.DeriveDefaultTag(url));
    }

    [Fact]
    public void BuildDisplayLabel_IncludesTag()
    {
        Assert.Equal("alice · local", VouchSigner.BuildDisplayLabel("alice", null, "http://localhost:5100"));
        Assert.Equal("alice · custom", VouchSigner.BuildDisplayLabel("alice", "custom", "http://localhost:5100"));
    }
}
