namespace Snekl.Billing.Tests;

public class BillingHistoryTests
{
    [Fact]
    public async Task MemoryStoreKeepsSubscriptionAndCommissionHistory() =>
        await BillingHistory.ExpectAsync(new MemoryBillingStore());

    [Fact]
    public async Task SqliteStoreKeepsSubscriptionAndCommissionHistory()
    {
        await using var database = new SqliteBillingDatabase();
        await BillingHistory.ExpectAsync(database.Store);
    }

    [Fact]
    public async Task FailedWebhookDoesNotConsumeTheEvent()
    {
        var store = new MemoryBillingStore();
        await store.RecordCommissionHoldAsync(BillingHistory.Hold("order_1", "pi_1"));
        var mismatch = BillingHistory.Held with { AmountCents = 5000, ArtistCents = 4840 };
        await Assert.ThrowsAsync<BillingException>(() => store.ApplyDeliveryAsync("evt_bad", "payment_intent.succeeded", mismatch));
        Assert.Equal(["commission_hold"], (await store.ModificationsAsync("commission", "order_1")).Select(row => row.Action));
        Assert.Equal(DeliveryOutcome.Applied, await store.ApplyDeliveryAsync("evt_bad", "payment_intent.succeeded", BillingHistory.Held));
        var commission = await store.CommissionAsync("order_1");
        Assert.Equal(2000, commission?.AmountCents);
        Assert.Equal("ch_1", commission?.StripeChargeId);
        Assert.Equal(["commission_hold", "commission_held"], (await store.ModificationsAsync("commission", "order_1")).Select(row => row.Action));
    }
}

public static class BillingHistory
{
    public static readonly CommissionHeld Held = new("order_1", "acct_artist", "pi_1", "ch_1", 2000, 160, 1840, "usd");

    public static CommissionHold Hold(string orderId, string paymentIntentId) =>
        new(orderId, "acct_artist", 2000, 160, 1840, "usd", paymentIntentId);

    public static async Task ExpectAsync(IBillingStore store)
    {
        await store.RecordSubscriptionCheckoutAsync(new SubscriptionCheckout("biz_1", "pat@acme.example", ["server-hosting", "support"], 7800, "cs_1"));
        Assert.Equal(DeliveryOutcome.Applied, await store.ApplyDeliveryAsync("evt_start", "checkout.session.completed", new SubscriptionStarted("biz_1", ["server-hosting", "support"], "sub_1", "cus_1")));
        var subscription = await store.SubscriptionAsync("biz_1");
        Assert.NotNull(subscription);
        Assert.Equal("active", subscription.Status);
        Assert.Equal("pat@acme.example", subscription.ContactEmail);
        Assert.Equal(7800, subscription.MonthlyAmountCents);
        Assert.Equal("cs_1", subscription.StripeSessionId);
        Assert.Equal("sub_1", subscription.StripeSubscriptionId);
        Assert.Equal("cus_1", subscription.StripeCustomerId);
        Assert.Equal(["server-hosting", "support"], subscription.Services);

        Assert.Equal(DeliveryOutcome.Applied, await store.ApplyDeliveryAsync("evt_fail", "invoice.payment_failed", new SubscriptionPaymentFailed("biz_1", "sub_1")));
        Assert.Equal("past_due", (await store.SubscriptionAsync("biz_1"))?.Status);
        Assert.Equal(DeliveryOutcome.Applied, await store.ApplyDeliveryAsync("evt_cancel", "customer.subscription.deleted", new SubscriptionCanceled("biz_1", "sub_1")));
        Assert.Equal(DeliveryOutcome.Duplicate, await store.ApplyDeliveryAsync("evt_cancel", "customer.subscription.deleted", null));
        Assert.Equal("canceled", (await store.SubscriptionAsync("biz_1"))?.Status);
        Assert.Equal(
            ["subscription_checkout", "business_subscription_started", "business_subscription_payment_failed", "business_subscription_canceled"],
            (await store.LedgerAsync("biz_1")).Select(row => row.Kind));

        var subscriptionMods = await store.ModificationsAsync("subscription", subscription.Id);
        Assert.Equal(
            ["subscription_checkout", "business_subscription_started", "business_subscription_payment_failed", "business_subscription_canceled"],
            subscriptionMods.Select(row => row.Action));
        Assert.Null(subscriptionMods[0].Before);
        Assert.Equal("api", subscriptionMods[0].Cause);
        Assert.Equal("checkout_open", subscriptionMods[0].After.GetProperty("status").GetString());
        Assert.Equal("webhook", subscriptionMods[1].Cause);
        Assert.Equal("evt_start", subscriptionMods[1].StripeEventId);
        Assert.Equal("checkout_open", subscriptionMods[1].Before?.GetProperty("status").GetString());
        Assert.Equal("active", subscriptionMods[1].After.GetProperty("status").GetString());
        var activation = subscriptionMods[1].Changes.Single(change => change.Field == "status");
        Assert.Equal("checkout_open", activation.From?.GetString());
        Assert.Equal("active", activation.To?.GetString());
        var subscriptionId = subscriptionMods[1].Changes.Single(change => change.Field == "stripeSubscriptionId");
        Assert.Null(subscriptionId.From);
        Assert.Equal("sub_1", subscriptionId.To?.GetString());

        await store.RecordCommissionHoldAsync(Hold("order_1", "pi_1"));
        Assert.Equal(DeliveryOutcome.Applied, await store.ApplyDeliveryAsync("evt_held", "payment_intent.succeeded", Held));
        var held = await store.CommissionAsync("order_1");
        Assert.Equal("held", held?.Status);
        Assert.Equal("ch_1", held?.StripeChargeId);
        Assert.Equal("pi_1", held?.StripePaymentIntentId);

        var released = await store.RecordCommissionReleaseAsync(new CommissionRelease("order_1", "acct_artist", 2000, 160, "ch_1", "tr_1"));
        Assert.Equal("released", released.Status);
        Assert.Equal(1840, released.ArtistCents);
        Assert.Equal("tr_1", released.StripeTransferId);
        Assert.Equal(DeliveryOutcome.Applied, await store.ApplyDeliveryAsync("evt_release", "transfer.created", new CommissionReleased("order_1", "acct_artist", "tr_1", 1840, 160)));

        var orderMods = await store.ModificationsAsync("commission", "order_1");
        Assert.Equal(["commission_hold", "commission_held", "commission_release"], orderMods.Select(row => row.Action));
        Assert.Null(orderMods[0].Before);
        Assert.Equal("held", orderMods[0].After.GetProperty("status").GetString());
        Assert.Equal(2000, orderMods[0].After.GetProperty("amountCents").GetInt32());
        Assert.Equal(160, orderMods[0].After.GetProperty("platformFeeCents").GetInt32());
        Assert.Equal("held", orderMods[2].Before?.GetProperty("status").GetString());
        Assert.Equal("released", orderMods[2].After.GetProperty("status").GetString());
        Assert.Equal("api", orderMods[2].Cause);
        Assert.Null(orderMods[2].StripeEventId);

        await store.RecordCommissionHoldAsync(Hold("order_2", "pi_2"));
        var refunded = await store.RecordCommissionRefundAsync(new CommissionRefund("order_2", "re_1"));
        Assert.Equal("refunded", refunded.Status);
        Assert.Equal(
            ["commission_hold", "commission_held", "commission_release", "commission_released"],
            (await store.LedgerAsync("order_1")).Select(row => row.Kind));
        Assert.Equal(["commission_hold", "commission_refund"], (await store.LedgerAsync("order_2")).Select(row => row.Kind));
        Assert.Equal(["commission_hold", "commission_refund"], (await store.ModificationsAsync("commission", "order_2")).Select(row => row.Action));

        Assert.Equal(DeliveryOutcome.Ignored, await store.ApplyDeliveryAsync("evt_other", "customer.created", null));
        var missing = await Assert.ThrowsAsync<BillingException>(() => store.RecordCommissionReleaseAsync(new CommissionRelease("missing", "acct_artist", 2000, 160, "ch_x", "tr_x")));
        Assert.Equal("not_held", missing.Code);
    }
}
