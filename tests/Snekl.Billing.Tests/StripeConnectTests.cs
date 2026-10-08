using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Snekl.Billing.Tests;

public class StripeConnectTests
{
    [Fact]
    public async Task OnboardingCreatesAStandardAccountAndReusesIt()
    {
        var handler = new QueueHandler();
        handler.Enqueue(200, new { id = "acct_1", email = "artist@example.com" });
        handler.Enqueue(200, new { url = "https://connect.stripe.com/setup/s/test" });
        handler.Enqueue(200, new { url = "https://connect.stripe.com/setup/s/again" });
        var store = new MemoryBillingStore();
        var api = Client(store, handler);
        var first = await api.BeginArtistOnboardingAsync("artist_1", "artist@example.com", "https://snekl.example/return", "https://snekl.example/refresh");
        Assert.Equal(("acct_1", "https://connect.stripe.com/setup/s/test"), first);
        Assert.Contains("type=standard", handler.Calls[0].Body, StringComparison.Ordinal);
        Assert.Contains("snekl_artist_id", handler.Calls[0].Body, StringComparison.Ordinal);
        Assert.Contains("transfers", handler.Calls[0].Body, StringComparison.Ordinal);
        Assert.Equal("connect_account_artist_1", handler.Calls[0].Request.Headers.GetValues("Idempotency-Key").Single());
        var saved = await store.ConnectedAccountAsync("artist_1");
        Assert.Equal("acct_1", saved?.StripeAccountId);
        Assert.False(saved?.PayoutsEnabled);

        var second = await api.BeginArtistOnboardingAsync("artist_1", null, "https://snekl.example/return", "https://snekl.example/refresh");
        Assert.Equal("https://connect.stripe.com/setup/s/again", second.Url);
        Assert.Equal(["https://api.stripe.com/v1/accounts", "https://api.stripe.com/v1/account_links", "https://api.stripe.com/v1/account_links"], handler.Calls.Select(call => call.Url));
    }

    [Fact]
    public async Task OAuthStoresTheConnectedAccountAndHidesTheToken()
    {
        var handler = new QueueHandler();
        handler.Enqueue(200, new { stripe_user_id = "acct_artist", access_token = "sk_live_artist_secret", refresh_token = "rt_secret" });
        handler.Enqueue(200, new { id = "acct_artist", email = "artist@example.com", details_submitted = true, charges_enabled = true, payouts_enabled = false });
        var store = new MemoryBillingStore();
        var api = Client(store, handler, debugHttp: true);
        var url = api.ArtistOAuthUrl("https://snekl.example/stripe/connect/callback", "state_1");
        Assert.Contains("client_id=ca_test", url, StringComparison.Ordinal);
        Assert.Contains("response_type=code", url, StringComparison.Ordinal);
        var connected = await api.CompleteArtistOAuthAsync("artist_1", "ac_secret_code");
        Assert.Equal("acct_artist", connected.StripeAccountId);
        Assert.True(connected.DetailsSubmitted);
        Assert.False(connected.PayoutsEnabled);
        Assert.Equal("https://connect.stripe.com/oauth/token", handler.Calls[0].Url);
        var recorded = JsonSerializer.Serialize(await store.HttpEventsAsync(), BillingJson.Options);
        Assert.DoesNotContain("ac_secret_code", recorded, StringComparison.Ordinal);
        Assert.DoesNotContain("sk_live_artist_secret", recorded, StringComparison.Ordinal);
        Assert.DoesNotContain("rt_secret", recorded, StringComparison.Ordinal);
        Assert.Throws<BillingException>(() => new StripeBillingClient(store, new HttpClient(new QueueHandler()), "sk_test_123", "whsec_test").ArtistOAuthUrl("https://snekl.example/back", "state_1"));
    }

    [Fact]
    public async Task AccountUpdatedMarksPayoutsReady()
    {
        var store = new MemoryBillingStore();
        await store.SaveConnectedAccountAsync(new ConnectedAccountUpdate("artist_1", "acct_1", "artist@example.com", false, false, false));
        var update = Assert.IsType<AccountUpdated>(StripeWebhook.Interpret("account.updated", JsonNode.Parse("""
            {"id":"acct_1","email":"artist@example.com","details_submitted":true,"charges_enabled":true,"payouts_enabled":true,"metadata":{"snekl_artist_id":"artist_1"}}
            """) as JsonObject));
        Assert.Equal(DeliveryOutcome.Applied, await store.ApplyDeliveryAsync("evt_acct", "account.updated", update));
        var account = await store.ConnectedAccountAsync("artist_1");
        Assert.True(account?.PayoutsEnabled);
        Assert.True(account?.DetailsSubmitted);
        Assert.Equal(DeliveryOutcome.Applied, await store.ApplyDeliveryAsync("evt_other", "account.updated", new AccountUpdated(null, "acct_unknown", null, true, true, true)));
        Assert.Null(await store.ConnectedAccountAsync("missing"));
    }

    [Fact]
    public async Task SqliteRoundTripsAConnectedAccount()
    {
        await using var database = new SqliteBillingDatabase();
        await database.Store.SaveConnectedAccountAsync(new ConnectedAccountUpdate("artist_1", "acct_1", null, true, true, true));
        var account = await database.Store.ConnectedAccountAsync("artist_1");
        Assert.Equal("acct_1", account?.StripeAccountId);
        Assert.True(account?.PayoutsEnabled);
    }

    static StripeBillingClient Client(IBillingStore store, QueueHandler handler, bool debugHttp = false, string? connectClientId = "ca_test") =>
        new(store, new HttpClient(handler), "sk_test_123", "whsec_test", debugHttp, connectClientId);

    sealed class QueueHandler : HttpMessageHandler
    {
        readonly Queue<HttpResponseMessage> _responses = new();
        public List<(string Url, string Body, HttpRequestMessage Request)> Calls { get; } = [];

        public void Enqueue(int status, object body) =>
            _responses.Enqueue(new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.RequestUri?.ToString() ?? "", body, request));
            return _responses.Dequeue();
        }
    }
}
