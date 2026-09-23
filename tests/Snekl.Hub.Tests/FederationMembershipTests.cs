using Snekl.Hub.Domain;

namespace Snekl.Hub.Tests;

public class FederationMembershipTests
{
    [Theory]
    [InlineData(null, "origin")]
    [InlineData("", "origin")]
    [InlineData("origin", "origin")]
    [InlineData("Origin", "origin")]
    [InlineData("hub", "hub")]
    [InlineData("HUB", "hub")]
    public void NormalizeInitiator_AcceptsHubOrOrigin(string? value, string expected) =>
        Assert.Equal(expected, FederationMembership.NormalizeInitiator(value));

    [Fact]
    public void NormalizeInitiator_RejectsUnknownValues() =>
        Assert.Throws<InvalidOperationException>(() => FederationMembership.NormalizeInitiator("admin"));
}
