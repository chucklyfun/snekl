using System.Net;
using System.Text;
using System.Text.Json;

namespace Snekl.Billing.Tests;

public class StripeBillingClientTests
{
    [Fact]
    public async Task StartsASubscriptionCheckoutWithAnIdempotencyKey()
    {
        var handler = new QueueHandler();
        handler.Enqueue(200, new { id = "cs_1", url = "https://checkout.stripe.com/c/pay" });
        var store = new MemoryBillingStore();
        var api = Client(store, handler);
        var (sessionId, url) = await api.StartBusinessSubscriptionAsync("biz_1", ["managed-auth"], "pat@acme.example", "https://snekl.example/ok", "https://snekl.example/cancel");
        Assert.Equal(("cs_1", "https://checkout.stripe.com/c/pay"), (sessionId, url));
        Assert.Equal("https://api.stripe.com/v1/checkout/sessions", handler.Calls[0].Url);
        Assert.Equal("business_sub_biz_1", handler.Calls[0].Request.Headers.GetValues("Idempotency-Key").Single());
        Assert.Contains("unit_amount%5D=1900", handler.Calls[0].Body, StringComparison.Ordinal);
        Assert.Empty(await store.HttpEventsAsync());
        var record = await store.SubscriptionAsync("biz_1");
        Assert.Equal("checkout_open", record?.Status);
        Assert.Equal("pat@acme.example", record?.ContactEmail);
        Assert.Equal(["managed-auth"], record?.Services);
        Assert.Equal(1900, record?.MonthlyAmountCents);
        Assert.Equal("cs_1", record?.StripeSessionId);
        Assert.Equal(["subscription_checkout"], (await store.LedgerAsync("biz_1")).Select(row => row.Kind));
    }

    [Fact]
    public async Task HoldsReleasesAndRefundsACommissionWithoutMovingTheFee()
    {
        var handler = new QueueHandler();
        handler.Enqueue(200, new { id = "pi_1", client_secret = "pi_1_secret" });
        handler.Enqueue(200, new { id = "tr_1" });
        handler.Enqueue(200, new { id = "pi_2", client_secret = "pi_2_secret" });
        handler.Enqueue(200, new { id = "re_1" });
        var store = new MemoryBillingStore();
        var api = Client(store, handler);
        var held = await api.HoldCommissionAsync("order_1", 2000, "acct_artist");
        Assert.Equal(("pi_1", 160, 1840), (held.PaymentIntentId, held.PlatformFeeCents, held.ArtistCents));
        var released = await api.ReleaseCommissionAsync(new CommissionRelease("order_1", "acct_artist", held.AmountCents, held.PlatformFeeCents, "ch_1", ""));
        Assert.Equal(("tr_1", 1840, 160), released);
        Assert.Equal("released", (await store.CommissionAsync("order_1"))?.Status);
        Assert.Equal("tr_1", (await store.CommissionAsync("order_1"))?.StripeTransferId);
        Assert.Equal("ch_1", (await store.CommissionAsync("order_1"))?.StripeChargeId);
        await api.HoldCommissionAsync("order_2", 2000, "acct_artist");
        Assert.Equal("re_1", await api.RefundCommissionAsync("order_2", "pi_2"));
        Assert.Equal("refunded", (await store.CommissionAsync("order_2"))?.Status);
        Assert.Equal(
            [
                "https://api.stripe.com/v1/payment_intents",
                "https://api.stripe.com/v1/transfers",
                "https://api.stripe.com/v1/payment_intents",
                "https://api.stripe.com/v1/refunds",
            ],
            handler.Calls.Select(call => call.Url));
        Assert.Contains("amount=1840", handler.Calls[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SurfacesStripeErrorMessagesAndRefusesAPublishableKey()
    {
        var handler = new QueueHandler();
        handler.Enqueue(402, new { error = new { message = "Your card was declined.", client_secret = "pi_secret_value", number = "4242424242424242" } });
        var store = new MemoryBillingStore();
        var api = Client(store, handler);
        var error = await Assert.ThrowsAsync<BillingException>(() => api.HoldCommissionAsync("order_1", 2000, "acct_artist"));
        Assert.Equal("stripe_request", error.Code);
        Assert.Equal("Your card was declined.", error.Message);
        var httpEvent = Assert.Single(await store.HttpEventsAsync());
        Assert.Equal("outbound", httpEvent.Direction);
        Assert.Equal("/v1/payment_intents", httpEvent.Path);
        Assert.Equal("Your card was declined.", httpEvent.Problem);
        Assert.Equal(402, httpEvent.ResponseStatus);
        Assert.Equal("***", httpEvent.RequestHeaders["authorization"]);
        Assert.Equal("commission_hold_order_1", httpEvent.RequestHeaders["idempotency-key"]);
        var recorded = JsonSerializer.Serialize(httpEvent, BillingJson.Options);
        Assert.DoesNotContain("sk_test_123", recorded, StringComparison.Ordinal);
        Assert.DoesNotContain("pi_secret_value", recorded, StringComparison.Ordinal);
        Assert.DoesNotContain("4242424242424242", recorded, StringComparison.Ordinal);
        Assert.Contains("****4242", recorded, StringComparison.Ordinal);
        Assert.Throws<BillingException>(() => Client(new MemoryBillingStore(), new QueueHandler(), "pk_test_123"));
    }

    [Fact]
    public async Task WritesSuccessfulCallsOnlyWhenDebugIsOn()
    {
        var handler = new QueueHandler();
        handler.Enqueue(200, new { id = "cs_1", url = "https://checkout.stripe.com/c/pay" });
        var store = new MemoryBillingStore();
        var api = Client(store, handler, debugHttp: true);
        await api.StartBusinessSubscriptionAsync("biz_1", ["managed-auth"], "pat@acme.example", "https://snekl.example/ok", "https://snekl.example/cancel");
        var httpEvent = Assert.Single(await store.HttpEventsAsync());
        Assert.Equal("/v1/checkout/sessions", httpEvent.Path);
        Assert.Null(httpEvent.Problem);
        Assert.Equal(200, httpEvent.ResponseStatus);
        Assert.DoesNotContain("sk_test_123", JsonSerializer.Serialize(httpEvent, BillingJson.Options), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogsAWebhookThatCannotBeVerified()
    {
        var store = new MemoryBillingStore();
        var api = Client(store, new QueueHandler());
        var raw = """{"id":"evt_1","client_secret":"whsec_from_body","type":"payment_intent.succeeded"}""";
        var error = await Assert.ThrowsAsync<BillingException>(() => api.ReceiveWebhookAsync(raw, "t=1,v1=not-a-signature"));
        Assert.Equal("bad_signature", error.Code);
        var httpEvent = Assert.Single(await store.HttpEventsAsync());
        Assert.Equal("inbound", httpEvent.Direction);
        Assert.Equal("/webhook", httpEvent.Path);
        Assert.Equal("***", httpEvent.RequestHeaders["stripe-signature"]);
        Assert.Contains("signature", httpEvent.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("whsec_from_body", httpEvent.RequestBody, StringComparison.Ordinal);
    }

    static StripeBillingClient Client(IBillingStore store, QueueHandler handler, string secretKey = "sk_test_123", bool debugHttp = false) =>
        new(store, new HttpClient(handler), secretKey, "whsec_test", debugHttp);

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
