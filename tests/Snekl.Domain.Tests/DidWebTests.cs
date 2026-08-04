using Snekl.Domain.Identity.Did;

namespace Snekl.Domain.Tests;

public class DidWebTests
{
    [Theory]
    [InlineData("http://localhost:5100", "did:web:localhost%3A5100")]
    [InlineData("https://auth.example.com/", "did:web:auth.example.com")]
    [InlineData("https://auth.example.com:443/", "did:web:auth.example.com")]
    public void FromBaseUrl_BuildsExpectedDid(string baseUrl, string expected)
    {
        Assert.Equal(expected, DidWeb.FromBaseUrl(baseUrl));
    }

    [Fact]
    public void DocumentUrl_PointsAtWellKnown()
    {
        Assert.Equal(
            "http://localhost:5100/.well-known/did.json",
            DidWeb.DocumentUrl("http://localhost:5100/"));
    }
}
