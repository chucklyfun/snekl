using System.Text.Json;

namespace Snekl.Billing;

/// <summary>In-memory billing book. The EF store loads and saves this same state.</summary>
public sealed class BillingState
{
    static readonly string[] SubscriptionFields = ["status", "monthlyAmountCents", "stripeSessionId", "stripeSubscriptionId", "stripeCustomerId", "services", "contactEmail", "currency"];
    static readonly string[] CommissionFields = ["status", "amountCents", "platformFeeCents", "artistCents", "currency", "artistAccountId", "stripePaymentIntentId", "stripeChargeId", "stripeTransferId", "stripeRefundId"];
    static readonly string[] ConnectedFields = ["stripeAccountId", "email", "detailsSubmitted", "chargesEnabled", "payoutsEnabled"];

    public List<SubscriptionRecord> Subscriptions { get; } = [];
    public List<CommissionRecord> Commissions { get; } = [];
    public List<LedgerEntry> Ledger { get; } = [];
    public List<PaymentModification> Modifications { get; } = [];
    public List<HttpEvent> HttpEvents { get; } = [];
    public List<ConnectedAccountRecord> ConnectedAccounts { get; } = [];
    public List<(string Id, string Type, string Outcome, DateTime At)> StripeEvents { get; } = [];

    public SubscriptionRecord RecordSubscriptionCheckout(SubscriptionCheckout checkout)
    {
        var at = Now();
        var record = new SubscriptionRecord(NewId(), checkout.BusinessAccountId, checkout.ContactEmail, checkout.Services.ToList(), "checkout_open", checkout.MonthlyAmountCents, "usd", checkout.StripeSessionId, null, null, at, at);
        Subscriptions.Add(record);
        PushLedger(record.Id, null, "subscription_checkout", checkout.BusinessAccountId, checkout.MonthlyAmountCents, record, at);
        PushModification("subscription", record.Id, "api", "subscription_checkout", null, null, record, at);
        return record;
    }

    public CommissionRecord RecordCommissionHold(CommissionHold hold)
    {
        var at = Now();
        var current = FindCommission(hold.OrderId);
        if (current is { Status: not "held" }) throw new BillingException("already_closed", "That commission escrow is already closed.");
        if (current?.StripePaymentIntentId == hold.StripePaymentIntentId) return current;
        if (current is not null) throw new BillingException("amount_mismatch", "That commission already has a different held payment.");
        var record = new CommissionRecord(hold.OrderId, hold.ArtistAccountId, "held", hold.AmountCents, hold.PlatformFeeCents, hold.ArtistCents, hold.Currency, hold.StripePaymentIntentId, null, null, null, at, at);
        Commissions.Add(record);
        PushLedger(NewId(), null, "commission_hold", hold.OrderId, hold.AmountCents, record, at);
        PushModification("commission", record.OrderId, "api", "commission_hold", null, null, record, at);
        return record;
    }

    public CommissionRecord RecordCommissionRelease(CommissionRelease release)
    {
        var at = Now();
        var current = FindCommission(release.OrderId);
        var gate = ReleaseGate(current, release);
        if (current is null) throw new BillingException("not_held", "Escrow is not held for that commission.");
        if (gate == "done")
        {
            if (current.StripeTransferId == release.StripeTransferId) return current;
            throw new BillingException("already_closed", "That commission escrow is already closed.");
        }
        var record = current with { Status = "released", StripeChargeId = current.StripeChargeId ?? release.ChargeId, StripeTransferId = release.StripeTransferId, UpdatedAt = at };
        Replace(record);
        PushLedger(NewId(), null, "commission_release", release.OrderId, record.ArtistCents, record, at);
        PushModification("commission", record.OrderId, "api", "commission_release", null, current, record, at);
        return record;
    }

    public CommissionRecord RecordCommissionRefund(CommissionRefund refund)
    {
        var at = Now();
        var current = FindCommission(refund.OrderId);
        var gate = RefundGate(current);
        if (current is null) throw new BillingException("not_held", "Escrow is not held for that commission.");
        if (gate == "done")
        {
            if (current.StripeRefundId is not null && current.StripeRefundId != refund.StripeRefundId)
                throw new BillingException("already_closed", "That commission escrow is already closed.");
            var updated = current with { StripeRefundId = refund.StripeRefundId };
            if (updated == current) return current;
            Replace(updated);
            PushModification("commission", updated.OrderId, "api", "commission_refund", null, current, updated, at);
            return updated;
        }
        var record = current with { Status = "refunded", StripeRefundId = refund.StripeRefundId, UpdatedAt = at };
        Replace(record);
        PushLedger(NewId(), null, "commission_refund", refund.OrderId, record.AmountCents, record, at);
        PushModification("commission", record.OrderId, "api", "commission_refund", null, current, record, at);
        return record;
    }

    public static string ReleaseGate(CommissionRecord? current, CommissionRelease release)
    {
        if (current is not { Status: "held" or "released" }) throw new BillingException("not_held", "Escrow is not held for that commission.");
        SameMoney(current, release.AmountCents, release.PlatformFeeCents, release.ArtistAccountId);
        if (current.StripeChargeId is not null && current.StripeChargeId != release.ChargeId)
            throw new BillingException("amount_mismatch", "The release does not match the held charge.");
        if (current.Status == "released")
        {
            if (current.StripeTransferId is not null) return "done";
            throw new BillingException("already_closed", "That commission escrow is already closed.");
        }
        return "ready";
    }

    public static string RefundGate(CommissionRecord? current)
    {
        if (current is not { Status: "held" or "refunded" }) throw new BillingException("not_held", "Escrow is not held for that commission.");
        return current.Status == "refunded" ? "done" : "ready";
    }

    public DeliveryOutcome ApplyDelivery(string stripeEventId, string type, BillingEvent? billingEvent)
    {
        if (StripeEvents.Any(row => row.Id == stripeEventId)) return DeliveryOutcome.Duplicate;
        var at = Now();
        if (billingEvent is null)
        {
            StripeEvents.Add((stripeEventId, type, "ignored", at));
            return DeliveryOutcome.Ignored;
        }
        switch (billingEvent)
        {
            case SubscriptionStarted or SubscriptionCanceled or SubscriptionPaymentFailed:
                ApplySubscription(stripeEventId, billingEvent, at);
                break;
            case CommissionHeld or CommissionReleased or CommissionRefunded:
                ApplyCommission(stripeEventId, billingEvent, at);
                break;
            case AccountUpdated updated:
                ApplyConnected(stripeEventId, updated, at);
                break;
            default:
                throw new BillingException("malformed_event", "Unexpected billing event.");
        }
        StripeEvents.Add((stripeEventId, type, "applied", at));
        return DeliveryOutcome.Applied;
    }

    public ConnectedAccountRecord SaveConnectedAccount(ConnectedAccountUpdate update)
    {
        var at = Now();
        var current = ConnectedAccounts.FirstOrDefault(row => row.ArtistId == update.ArtistId);
        var other = ConnectedAccounts.FirstOrDefault(row => row.StripeAccountId == update.StripeAccountId);
        if (other is not null && other.ArtistId != update.ArtistId)
            throw new BillingException("already_connected", "That Stripe account is already connected to another artist.");
        if (current is not null && current.StripeAccountId != update.StripeAccountId)
            throw new BillingException("already_connected", "This artist already has a Stripe account.");
        return WriteConnected(current, update, "api", update.ArtistId == current?.ArtistId && current is not null ? "connected_account_updated" : "connected_account_connected", null, at);
    }

    public void RecordHttp(HttpEventDraft draft) =>
        HttpEvents.Add(new HttpEvent(NewId(), draft.Direction, draft.Method, draft.Path, draft.RequestHeaders, draft.RequestBody, draft.ResponseStatus, draft.ResponseHeaders, draft.ResponseBody, draft.Problem, Now()));

    public SubscriptionRecord? LatestSubscription(string businessAccountId) =>
        Subscriptions.Where(row => row.BusinessAccountId == businessAccountId).OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).FirstOrDefault();

    void ApplyConnected(string stripeEventId, AccountUpdated update, DateTime at)
    {
        var current = ConnectedAccounts.FirstOrDefault(row => row.StripeAccountId == update.StripeAccountId);
        if (current is null && update.ArtistId is not null)
        {
            var owned = ConnectedAccounts.FirstOrDefault(row => row.ArtistId == update.ArtistId);
            if (owned is null)
            {
                WriteConnected(null, new ConnectedAccountUpdate(update.ArtistId, update.StripeAccountId, update.Email, update.DetailsSubmitted, update.ChargesEnabled, update.PayoutsEnabled), "webhook", "connected_account_updated", stripeEventId, at);
                return;
            }
            if (owned.StripeAccountId != update.StripeAccountId) return;
            current = owned;
        }
        if (current is null) return;
        WriteConnected(current, new ConnectedAccountUpdate(current.ArtistId, current.StripeAccountId, update.Email ?? current.Email, update.DetailsSubmitted, update.ChargesEnabled, update.PayoutsEnabled), "webhook", "connected_account_updated", stripeEventId, at);
    }

    ConnectedAccountRecord WriteConnected(ConnectedAccountRecord? current, ConnectedAccountUpdate update, string cause, string action, string? stripeEventId, DateTime at)
    {
        var next = new ConnectedAccountRecord(update.ArtistId, update.StripeAccountId, update.Email ?? current?.Email, update.DetailsSubmitted, update.ChargesEnabled, update.PayoutsEnabled, current?.CreatedAt ?? at, at);
        var index = current is null ? -1 : ConnectedAccounts.FindIndex(row => row.ArtistId == current.ArtistId);
        if (index >= 0) ConnectedAccounts[index] = next;
        else ConnectedAccounts.Add(next);
        PushLedger(NewId(), stripeEventId, action, next.ArtistId, null, next, at);
        PushModification("connected_account", next.ArtistId, cause, action, stripeEventId, current, next, at);
        return next;
    }

    void ApplySubscription(string stripeEventId, BillingEvent billingEvent, DateTime at)
    {
        var next = billingEvent switch
        {
            SubscriptionStarted started => ApplyStarted(started, at),
            SubscriptionCanceled canceled => ApplyCanceled(canceled, at),
            SubscriptionPaymentFailed failed => ApplyFailed(failed, at),
            _ => throw new BillingException("malformed_event", "Unexpected subscription event."),
        };
        var before = Subscriptions.FirstOrDefault(row => row.Id == next.Id);
        var index = Subscriptions.FindIndex(row => row.Id == next.Id);
        if (index >= 0) Subscriptions[index] = next;
        else Subscriptions.Add(next);
        var subject = next.BusinessAccountId ?? next.StripeSubscriptionId ?? next.Id;
        PushLedger(NewId(), stripeEventId, ActionName(billingEvent), subject, next.MonthlyAmountCents, next, at);
        PushModification("subscription", next.Id, "webhook", ActionName(billingEvent), stripeEventId, before, next, at);
    }

    SubscriptionRecord ApplyStarted(SubscriptionStarted started, DateTime at)
    {
        var existing = Subscriptions.FirstOrDefault(row => row.StripeSubscriptionId == started.SubscriptionId)
            ?? Newest(Subscriptions.Where(row => row.BusinessAccountId == started.BusinessAccountId && row.Status == "checkout_open"));
        if (existing is null) return Blank(started.BusinessAccountId, started.Services, "active", started.SubscriptionId, started.CustomerId, at);
        return existing with { Services = started.Services.ToList(), Status = "active", StripeSubscriptionId = started.SubscriptionId, StripeCustomerId = started.CustomerId, UpdatedAt = at };
    }

    SubscriptionRecord ApplyCanceled(SubscriptionCanceled canceled, DateTime at)
    {
        var existing = Subscriptions.FirstOrDefault(row => row.StripeSubscriptionId == canceled.SubscriptionId)
            ?? (canceled.BusinessAccountId is null ? null : Newest(Subscriptions.Where(row => row.BusinessAccountId == canceled.BusinessAccountId && row.Status != "canceled")));
        if (existing is null) return Blank(canceled.BusinessAccountId, [], "canceled", canceled.SubscriptionId, null, at);
        return existing with { Status = "canceled", UpdatedAt = at };
    }

    SubscriptionRecord ApplyFailed(SubscriptionPaymentFailed failed, DateTime at)
    {
        var existing = Subscriptions.FirstOrDefault(row => row.StripeSubscriptionId == failed.SubscriptionId);
        if (existing is null) return Blank(failed.BusinessAccountId, [], "past_due", failed.SubscriptionId, null, at);
        if (existing.Status == "canceled") return existing;
        return existing with { Status = "past_due", UpdatedAt = at };
    }

    void ApplyCommission(string stripeEventId, BillingEvent billingEvent, DateTime at)
    {
        var orderId = billingEvent switch
        {
            CommissionHeld held => held.OrderId,
            CommissionReleased released => released.OrderId,
            CommissionRefunded refunded => refunded.OrderId,
            _ => throw new BillingException("malformed_event", "Unexpected commission event."),
        };
        var before = FindCommission(orderId);
        var next = billingEvent switch
        {
            CommissionHeld held => ApplyHeld(before, held, at),
            CommissionReleased released => ApplyReleased(before, released, at),
            CommissionRefunded refunded => ApplyRefunded(before, refunded, at),
            _ => throw new BillingException("malformed_event", "Unexpected commission event."),
        };
        Replace(next);
        var amount = billingEvent is CommissionReleased ? next.ArtistCents : next.AmountCents;
        PushLedger(NewId(), stripeEventId, ActionName(billingEvent), next.OrderId, amount, next, at);
        PushModification("commission", next.OrderId, "webhook", ActionName(billingEvent), stripeEventId, before, next, at);
    }

    static CommissionRecord ApplyHeld(CommissionRecord? current, CommissionHeld held, DateTime at)
    {
        if (current is null)
            return new CommissionRecord(held.OrderId, held.ArtistAccountId, "held", held.AmountCents, held.PlatformFeeCents, held.ArtistCents, held.Currency, held.PaymentIntentId, held.ChargeId, null, null, at, at);
        SameMoney(current, held.AmountCents, held.PlatformFeeCents, held.ArtistAccountId);
        if (current.StripePaymentIntentId is not null && current.StripePaymentIntentId != held.PaymentIntentId)
            throw new BillingException("amount_mismatch", "The held commission does not match the Stripe payment.");
        if (current.StripeChargeId is not null && current.StripeChargeId != held.ChargeId)
            throw new BillingException("amount_mismatch", "The held commission does not match the Stripe charge.");
        return current with { StripePaymentIntentId = held.PaymentIntentId, StripeChargeId = held.ChargeId, Currency = held.Currency, UpdatedAt = at };
    }

    static CommissionRecord ApplyReleased(CommissionRecord? current, CommissionReleased released, DateTime at)
    {
        if (current is null || current.Status == "refunded") throw new BillingException("not_held", "Escrow is not held for that commission.");
        SameMoney(current, current.AmountCents, released.PlatformFeeCents, released.ArtistAccountId);
        if (released.ArtistCents != current.ArtistCents) throw new BillingException("amount_mismatch", "The release does not match the held commission.");
        if (current.Status == "released")
        {
            if (current.StripeTransferId == released.TransferId) return current;
            throw new BillingException("already_closed", "That commission escrow is already closed.");
        }
        return current with { Status = "released", StripeTransferId = released.TransferId, UpdatedAt = at };
    }

    static CommissionRecord ApplyRefunded(CommissionRecord? current, CommissionRefunded refunded, DateTime at)
    {
        if (current is null) throw new BillingException("not_held", "Escrow is not held for that commission.");
        if (refunded.AmountCents != current.AmountCents) throw new BillingException("amount_mismatch", "The refund does not match the held commission.");
        if (current.StripeChargeId is not null && current.StripeChargeId != refunded.ChargeId)
            throw new BillingException("amount_mismatch", "The refund does not match the held charge.");
        if (current.Status == "refunded") return current;
        return current with { Status = "refunded", StripeChargeId = current.StripeChargeId ?? refunded.ChargeId, UpdatedAt = at };
    }

    void PushLedger(string id, string? stripeEventId, string kind, string subjectId, int? amountCents, object detail, DateTime at) =>
        Ledger.Add(new LedgerEntry(id, stripeEventId, kind, subjectId, amountCents, JsonSerializer.Serialize(detail, BillingJson.Options), at));

    void PushModification(string paymentKind, string paymentId, string cause, string action, string? stripeEventId, object? before, object after, DateTime at)
    {
        var fields = paymentKind switch
        {
            "subscription" => SubscriptionFields,
            "commission" => CommissionFields,
            "connected_account" => ConnectedFields,
            _ => throw new BillingException("record", "Unexpected payment kind."),
        };
        JsonElement? beforeJson = before is null ? null : BillingJson.Element(before);
        var afterJson = BillingJson.Element(after);
        var changes = new List<FieldChange>();
        foreach (var field in fields)
        {
            var from = beforeJson is null ? null : BillingJson.Property(beforeJson.Value, field);
            var to = BillingJson.Property(afterJson, field);
            if (BillingJson.Same(from, to)) continue;
            changes.Add(new FieldChange(field, from, to));
        }
        if (changes.Count == 0) return;
        Modifications.Add(new PaymentModification(NewId(), paymentKind, paymentId, cause, action, stripeEventId, beforeJson, afterJson, changes, at));
    }

    CommissionRecord Replace(CommissionRecord record)
    {
        var index = Commissions.FindIndex(row => row.OrderId == record.OrderId);
        if (index >= 0) Commissions[index] = record;
        else Commissions.Add(record);
        return record;
    }

    CommissionRecord? FindCommission(string orderId) => Commissions.FirstOrDefault(row => row.OrderId == orderId);

    static SubscriptionRecord Blank(string? businessAccountId, IReadOnlyList<string> services, string status, string stripeSubscriptionId, string? stripeCustomerId, DateTime at) =>
        new(NewId(), businessAccountId, null, services.ToList(), status, null, "usd", null, stripeSubscriptionId, stripeCustomerId, at, at);

    static SubscriptionRecord? Newest(IEnumerable<SubscriptionRecord> rows) =>
        rows.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).FirstOrDefault();

    static void SameMoney(CommissionRecord current, int amountCents, int platformFeeCents, string artistAccountId)
    {
        if (current.AmountCents != amountCents || current.PlatformFeeCents != platformFeeCents || current.ArtistCents != amountCents - platformFeeCents)
            throw new BillingException("amount_mismatch", "The commission amount does not match the held record.");
        if (current.ArtistAccountId != artistAccountId)
            throw new BillingException("amount_mismatch", "The commission artist does not match the held record.");
    }

    static string ActionName(BillingEvent billingEvent) => billingEvent switch
    {
        SubscriptionStarted => "business_subscription_started",
        SubscriptionCanceled => "business_subscription_canceled",
        SubscriptionPaymentFailed => "business_subscription_payment_failed",
        CommissionHeld => "commission_held",
        CommissionReleased => "commission_released",
        CommissionRefunded => "commission_refunded",
        AccountUpdated => "connected_account_updated",
        _ => throw new BillingException("malformed_event", "Unexpected billing event."),
    };

    DateTime Now()
    {
        var next = DateTime.UtcNow;
        DateTime? latest = null;
        void Consider(DateTime value)
        {
            if (latest is null || value > latest) latest = value;
        }
        foreach (var row in Subscriptions) { Consider(row.CreatedAt); Consider(row.UpdatedAt); }
        foreach (var row in Commissions) { Consider(row.CreatedAt); Consider(row.UpdatedAt); }
        foreach (var row in Ledger) Consider(row.CreatedAt);
        foreach (var row in Modifications) Consider(row.CreatedAt);
        foreach (var row in HttpEvents) Consider(row.CreatedAt);
        foreach (var row in StripeEvents) Consider(row.At);
        foreach (var row in ConnectedAccounts) { Consider(row.CreatedAt); Consider(row.UpdatedAt); }
        if (latest is not null && next <= latest) next = latest.Value.AddMilliseconds(1);
        return next;
    }
    static string NewId() => Guid.NewGuid().ToString();
}

public static class BillingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value, value.GetType(), Options);

    public static JsonElement? Property(JsonElement element, string camelName)
    {
        if (!element.TryGetProperty(camelName, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        return property;
    }

    public static bool Same(JsonElement? left, JsonElement? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        return left.Value.GetRawText() == right.Value.GetRawText();
    }
}
