using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Snekl.Billing.Tests;

public class StripeWebhookTests
{
    const string Secret = "whsec_test_secret";
    static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

    [Fact]
    public void AcceptsASignatureProducedTheWayStripeSigns()
    {
        var body = """
            {"id":"evt_1","type":"payment_intent.succeeded","data":{"object":{"id":"pi_1","amount":2000,"currency":"usd","latest_charge":"ch_1","metadata":{"snekl_kind":"commission_escrow","order_id":"order_1","artist_account_id":"acct_artist","platform_fee_cents":"160","artist_cents":"1840"}}}}
            """;
        var result = StripeWebhook.Read(body, Signed(body), Secret, Now);
        var held = Assert.IsType<CommissionHeld>(result.Event);
        Assert.Equal("order_1", held.OrderId);
        Assert.Equal("acct_artist", held.ArtistAccountId);
        Assert.Equal("ch_1", held.ChargeId);
        Assert.Equal(1840, held.ArtistCents);
    }

    [Fact]
    public void RejectsAStaleOrForgedSignature()
    {
        var body = """{"id":"evt_1","type":"charge.refunded","data":{"object":{"id":"ch_1"}}}""";
        Assert.Equal("bad_signature", Assert.Throws<BillingException>(() => StripeWebhook.Read(body, Signed(body, Now.ToUnixTimeSeconds() - 600), Secret, Now)).Code);
        Assert.Throws<BillingException>(() => StripeWebhook.Read(body, Signed(body).Replace("v1=", "v1=00", StringComparison.Ordinal), Secret, Now));
    }

    [Fact]
    public void MapsCheckoutCancelFailureReleaseAndRefund()
    {
        var started = Assert.IsType<SubscriptionStarted>(StripeWebhook.Interpret("checkout.session.completed", Object("""
            {"metadata":{"snekl_kind":"business_subscription","business_account_id":"biz_1","services":"server-hosting,support"},"subscription":"sub_1","customer":{"id":"cus_1"}}
            """)));
        Assert.Equal("biz_1", started.BusinessAccountId);
        Assert.Equal(["server-hosting", "support"], started.Services);
        Assert.Equal("sub_1", started.SubscriptionId);
        Assert.Equal("cus_1", started.CustomerId);

        var canceled = Assert.IsType<SubscriptionCanceled>(StripeWebhook.Interpret("customer.subscription.deleted", Object("""
            {"id":"sub_1","metadata":{"snekl_kind":"business_subscription","business_account_id":"biz_1"}}
            """)));
        Assert.Equal("sub_1", canceled.SubscriptionId);

        var failed = Assert.IsType<SubscriptionPaymentFailed>(StripeWebhook.Interpret("invoice.payment_failed", Object("""
            {"subscription":"sub_1","parent":{"subscription_details":{"metadata":{"snekl_kind":"business_subscription","business_account_id":"biz_1"}}}}
            """)));
        Assert.Equal("biz_1", failed.BusinessAccountId);
        Assert.Equal("sub_1", failed.SubscriptionId);

        Assert.Null(StripeWebhook.Interpret("invoice.payment_failed", Object("""{"subscription":"sub_other"}""")));
        Assert.Null(StripeWebhook.Interpret("customer.created", Object("""{"id":"cus_1"}""")));

        var released = Assert.IsType<CommissionReleased>(StripeWebhook.Interpret("transfer.created", Object("""
            {"id":"tr_1","amount":1840,"metadata":{"snekl_kind":"commission_release","order_id":"order_1","artist_account_id":"acct_artist","platform_fee_cents":"160"}}
            """)));
        Assert.Equal("tr_1", released.TransferId);
        Assert.Equal(1840, released.ArtistCents);

        var refunded = Assert.IsType<CommissionRefunded>(StripeWebhook.Interpret("charge.refunded", Object("""
            {"id":"ch_1","amount_refunded":2000,"metadata":{"snekl_kind":"commission_escrow","order_id":"order_1"}}
            """)));
        Assert.Equal("order_1", refunded.OrderId);
        Assert.Equal(2000, refunded.AmountCents);
        Assert.Null(StripeWebhook.Interpret("charge.refunded", Object("""
            {"id":"ch_1","amount_refunded":0,"metadata":{"snekl_kind":"commission_escrow","order_id":"order_1"}}
            """)));
    }

    [Fact]
    public void RejectsAHeldChargeWhoseFeeDoesNotMatch()
    {
        var error = Assert.Throws<BillingException>(() => StripeWebhook.Interpret("payment_intent.succeeded", Object("""
            {"id":"pi_1","amount":2000,"currency":"usd","latest_charge":"ch_1","metadata":{"snekl_kind":"commission_escrow","order_id":"order_1","platform_fee_cents":"100","artist_cents":"100"}}
            """)));
        Assert.Equal("malformed_event", error.Code);
    }

    static JsonObject Object(string json) => JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("expected object");

    static string Signed(string body, long? timestamp = null)
    {
        var seconds = timestamp ?? Now.ToUnixTimeSeconds();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes($"{seconds}.{body}"));
        return $"t={seconds},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
