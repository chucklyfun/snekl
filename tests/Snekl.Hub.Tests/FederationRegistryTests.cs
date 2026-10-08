using Microsoft.EntityFrameworkCore;
using Snekl.Hub.Domain.Dtos;
using Snekl.Hub.Infrastructure.Data;
using Snekl.Hub.Infrastructure.Services;

namespace Snekl.Hub.Tests;

public class FederationRegistryTests
{
    private static FederationRegistry CreateRegistry(out HubDbContext db)
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        db = new HubDbContext(options);
        return new FederationRegistry(db);
    }

    [Fact]
    public async Task OriginRequest_StaysPendingUntilHubAccepts()
    {
        var registry = CreateRegistry(out _);
        var before = await registry.GetMapVersionAsync();

        var pending = await registry.RegisterServerAsync(
            new RegisterServerRequest("did:web:origin.test", "http://origin.test", "Origin", "origin"));

        Assert.Equal("pending", pending.Status);
        Assert.Equal("origin", pending.InitiatedBy);

        var map = await registry.GetMapAsync();
        Assert.Equal(before, map.MapVersion);
        Assert.Empty(map.Servers);
        Assert.Contains(await registry.ListJoinRequestsAsync(), s => s.ServerDid == "did:web:origin.test");

        var accepted = await registry.AcceptJoinAsync(new AcceptJoinRequest("did:web:origin.test", "hub"));
        Assert.Equal("active", accepted.Status);

        map = await registry.GetMapAsync();
        Assert.True(map.MapVersion > before);
        Assert.Contains(map.Servers, s => s.ServerDid == "did:web:origin.test");
        Assert.Empty(await registry.ListJoinRequestsAsync());
    }

    [Fact]
    public async Task HubInvite_StaysPendingUntilOriginAccepts()
    {
        var registry = CreateRegistry(out _);
        var pending = await registry.RegisterServerAsync(
            new RegisterServerRequest("did:web:invite.test", "http://invite.test", "Invite", "hub"));

        Assert.Equal("pending", pending.Status);
        Assert.Equal("hub", pending.InitiatedBy);
        Assert.Empty((await registry.GetMapAsync()).Servers);

        var accepted = await registry.AcceptJoinAsync(new AcceptJoinRequest("did:web:invite.test", "origin"));
        Assert.Equal("active", accepted.Status);
        Assert.Contains((await registry.GetMapAsync()).Servers, s => s.ServerDid == "did:web:invite.test");
    }

    [Fact]
    public async Task MutualRequests_ActivateMembership()
    {
        var registry = CreateRegistry(out _);
        await registry.RegisterServerAsync(
            new RegisterServerRequest("did:web:mutual.test", "http://mutual.test", "Mutual", "origin"));

        var result = await registry.RegisterServerAsync(
            new RegisterServerRequest("did:web:mutual.test", "http://mutual.test", "Mutual", "hub"));

        Assert.Equal("active", result.Status);
        Assert.Contains((await registry.GetMapAsync()).Servers, s => s.ServerDid == "did:web:mutual.test");
    }

    [Fact]
    public async Task SamePartyCannotAcceptOwnRequest()
    {
        var registry = CreateRegistry(out _);
        await registry.RegisterServerAsync(
            new RegisterServerRequest("did:web:own.test", "http://own.test", "Own", "origin"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.AcceptJoinAsync(new AcceptJoinRequest("did:web:own.test", "origin")));
        Assert.Contains("other party", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty((await registry.GetMapAsync()).Servers);
    }

    [Fact]
    public async Task Decline_RemovesPendingRequest()
    {
        var registry = CreateRegistry(out _);
        await registry.RegisterServerAsync(
            new RegisterServerRequest("did:web:decline.test", "http://decline.test", "Decline", "hub"));

        await registry.DeclineJoinAsync(new DeclineJoinRequest("did:web:decline.test"));

        Assert.Empty(await registry.ListJoinRequestsAsync("did:web:decline.test"));
        Assert.Empty((await registry.GetMapAsync()).Servers);
    }
}
