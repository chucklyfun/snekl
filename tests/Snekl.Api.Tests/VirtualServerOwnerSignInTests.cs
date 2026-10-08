using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Snekl.Domain.Identity;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;
using Snekl.Tests.Shared;
using AccountDtos = Snekl.Account.Domain.Dtos;
using ContentDtos = Snekl.Domain.Dtos;

namespace Snekl.Api.Tests;

/// <summary>
/// Servers spawned from the repo's server-template.json inherit the host's auth servers live.
/// A business server always keeps the auth server it was created through, and servers refuse
/// tokens from issuers outside their effective list.
/// </summary>
[Collection(DualApiCollection.Name)]
public class VirtualServerOwnerSignInTests
{
    private const string ServerHeader = "X-Snekl-Server";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly DualApiFixture _fixture;

    public VirtualServerOwnerSignInTests(DualApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Owner_signs_in_and_trust_follows_the_effective_issuer_list()
    {
        var (username, did, token) = await RegisterAsync();
        using var content = _fixture.CreateContentClient();
        content.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Like the business flow, the member uses the host first, which creates a host shadow user.
        var hostMe = await content.GetAsync("/api/auth/me");
        Assert.True(hostMe.IsSuccessStatusCode, await hostMe.Content.ReadAsStringAsync());

        var spawned = new List<Guid>();
        List<Guid> deactivatedHostRows = [];
        try
        {
            // A business server pinned to the auth server its owner signed up through.
            var owned = await SpawnAsync($"Owned {username}", username, did, DualApiFixture.AuthBaseUrl);
            spawned.Add(owned.Id);
            // A plain spawned server that only inherits the host's list.
            var plain = await SpawnAsync($"Plain {username}", username, did);
            spawned.Add(plain.Id);
            // A server that opts out of inheriting and so trusts nothing.
            var isolated = await SpawnAsync($"Isolated {username}", $"{username}-x", "did:key:zSomeoneElse");
            spawned.Add(isolated.Id);
            await WithDbAsync(db => db.NodeConfig.Where(n => n.Id == isolated.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.InheritHostAuthIssuers, false)));

            var catalog = await GetAsync<ContentDtos.NodeAuthInfoDto>(content, "/api/node/auth", plain.Id);
            Assert.Contains(catalog.Issuers, i => i.IssuerUrl == DualApiFixture.AuthBaseUrl);

            var me = await GetAsync<ContentDtos.AuthMeResponse>(content, "/api/auth/me", owned.Id);
            Assert.True(me.IsServerOwner);
            Assert.Equal(owned.OwnerUserId, me.User.Id);
            Assert.Equal(username, me.User.Username);
            Assert.True((await GetAsync<ContentDtos.AuthMeResponse>(content, "/api/auth/me", plain.Id)).IsServerOwner);
            Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(content, "/api/auth/me", isolated.Id));

            // The host stops trusting the auth server: the plain server follows, the business server does not.
            deactivatedHostRows = await WithDbAsync(async db =>
            {
                var hostId = await db.NodeConfig.OrderBy(n => n.CreatedAt).Select(n => n.Id).FirstAsync();
                var rows = await db.TrustedAuthIssuers.IgnoreQueryFilters()
                    .Where(i => i.ServerId == hostId && i.IssuerUrl == DualApiFixture.AuthBaseUrl && i.IsActive)
                    .ToListAsync();
                rows.ForEach(r => r.IsActive = false);
                await db.SaveChangesAsync();
                return rows.Select(r => r.Id).ToList();
            });
            Assert.NotEmpty(deactivatedHostRows);

            Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(content, "/api/auth/me", plain.Id));
            Assert.True((await GetAsync<ContentDtos.AuthMeResponse>(content, "/api/auth/me", owned.Id)).IsServerOwner);
            var ownedCatalog = await GetAsync<ContentDtos.NodeAuthInfoDto>(content, "/api/node/auth", owned.Id);
            Assert.Contains(ownedCatalog.Issuers, i => i.IssuerUrl == DualApiFixture.AuthBaseUrl);
            var plainCatalog = await GetAsync<ContentDtos.NodeAuthInfoDto>(content, "/api/node/auth", plain.Id);
            Assert.DoesNotContain(plainCatalog.Issuers, i => i.IssuerUrl == DualApiFixture.AuthBaseUrl);
        }
        finally
        {
            await WithDbAsync(async db =>
            {
                if (deactivatedHostRows.Count > 0)
                {
                    await db.TrustedAuthIssuers.IgnoreQueryFilters()
                        .Where(i => deactivatedHostRows.Contains(i.Id))
                        .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsActive, true));
                }

                return 0;
            });

            // Other tests in this collection expect a single-server host.
            await using var scope = _fixture.ContentFactory.Services.CreateAsyncScope();
            var servers = scope.ServiceProvider.GetRequiredService<IVirtualServerService>();
            foreach (var id in spawned)
            {
                await servers.DisableAsync(id);
                await servers.PurgeAsync(id);
            }
        }
    }

    private async Task<T> WithDbAsync<T>(Func<SneklDbContext, Task<T>> action)
    {
        await using var scope = _fixture.ContentFactory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<SneklDbContext>());
    }

    private async Task<(string Username, string Did, string Token)> RegisterAsync()
    {
        var username = $"vs_{Guid.NewGuid():N}"[..12];
        var (keys, proof) = DidProofHelper.CreateRegistrationProof(username);
        var register = await _fixture.AuthClient.PostAsJsonAsync("/api/auth/register", new AccountDtos.RegisterRequest(
            username,
            $"{username}@example.com",
            "Password1!",
            "Server Owner",
            keys.Did,
            proof));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var session = await register.Content.ReadFromJsonAsync<AccountDtos.RingSessionDto>(JsonOptions);
        return (username, keys.Did, session!.AccessToken);
    }

    private async Task<VirtualServerSummary> SpawnAsync(string name, string ownerUsername, string ownerDid, string? requiredIssuer = null)
    {
        // Uses the host's template store, which reads src/Snekl.Api/server-template.json.
        await using var scope = _fixture.ContentFactory.Services.CreateAsyncScope();
        var servers = scope.ServiceProvider.GetRequiredService<IVirtualServerService>();
        return await servers.SpawnAsync(new SpawnServerRequest(name, ownerUsername, ownerDid, requiredIssuer));
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string path, Guid serverId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(ServerHeader, serverId.ToString());
        return (await client.SendAsync(request)).StatusCode;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path, Guid serverId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(ServerHeader, serverId.ToString());
        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} for {serverId} failed: {(int)response.StatusCode} {text}");
        return JsonSerializer.Deserialize<T>(text, JsonOptions)!;
    }
}
