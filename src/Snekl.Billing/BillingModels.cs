using System.Text.Json;

namespace Snekl.Billing;

public sealed record SubscriptionRecord(
    string Id,
    string? BusinessAccountId,
    string? ContactEmail,
    IReadOnlyList<string> Services,
    string Status,
    int? MonthlyAmountCents,
    string Currency,
    string? StripeSessionId,
    string? StripeSubscriptionId,
    string? StripeCustomerId,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CommissionRecord(
    string OrderId,
    string ArtistAccountId,
    string Status,
    int AmountCents,
    int PlatformFeeCents,
    int ArtistCents,
    string Currency,
    string? StripePaymentIntentId,
    string? StripeChargeId,
    string? StripeTransferId,
    string? StripeRefundId,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record LedgerEntry(string Id, string? StripeEventId, string Kind, string SubjectId, int? AmountCents, string Detail, DateTime CreatedAt);

public sealed record FieldChange(string Field, JsonElement? From, JsonElement? To);

public sealed record PaymentModification(
    string Id,
    string PaymentKind,
    string PaymentId,
    string Cause,
    string Action,
    string? StripeEventId,
    JsonElement? Before,
    JsonElement After,
    IReadOnlyList<FieldChange> Changes,
    DateTime CreatedAt);

public sealed record HttpEvent(
    string Id,
    string Direction,
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> RequestHeaders,
    string? RequestBody,
    int? ResponseStatus,
    IReadOnlyDictionary<string, string> ResponseHeaders,
    string? ResponseBody,
    string? Problem,
    DateTime CreatedAt);

public sealed record HttpEventDraft(
    string Direction,
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> RequestHeaders,
    string? RequestBody,
    int? ResponseStatus,
    IReadOnlyDictionary<string, string> ResponseHeaders,
    string? ResponseBody,
    string? Problem);

public sealed record SubscriptionCheckout(
    string BusinessAccountId,
    string ContactEmail,
    IReadOnlyList<string> Services,
    int MonthlyAmountCents,
    string StripeSessionId);

public sealed record CommissionHold(
    string OrderId,
    string ArtistAccountId,
    int AmountCents,
    int PlatformFeeCents,
    int ArtistCents,
    string Currency,
    string StripePaymentIntentId);

public sealed record CommissionRelease(
    string OrderId,
    string ArtistAccountId,
    int AmountCents,
    int PlatformFeeCents,
    string ChargeId,
    string StripeTransferId);

public sealed record CommissionRefund(string OrderId, string StripeRefundId);

public sealed record ConnectedAccountRecord(
    string ArtistId,
    string StripeAccountId,
    string? Email,
    bool DetailsSubmitted,
    bool ChargesEnabled,
    bool PayoutsEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ConnectedAccountUpdate(
    string ArtistId,
    string StripeAccountId,
    string? Email,
    bool DetailsSubmitted,
    bool ChargesEnabled,
    bool PayoutsEnabled);

public abstract record BillingEvent;

public sealed record SubscriptionStarted(string BusinessAccountId, IReadOnlyList<string> Services, string SubscriptionId, string CustomerId) : BillingEvent;
public sealed record SubscriptionCanceled(string? BusinessAccountId, string SubscriptionId) : BillingEvent;
public sealed record SubscriptionPaymentFailed(string? BusinessAccountId, string SubscriptionId) : BillingEvent;
public sealed record CommissionHeld(string OrderId, string ArtistAccountId, string PaymentIntentId, string ChargeId, int AmountCents, int PlatformFeeCents, int ArtistCents, string Currency) : BillingEvent;
public sealed record CommissionReleased(string OrderId, string ArtistAccountId, string TransferId, int ArtistCents, int PlatformFeeCents) : BillingEvent;
public sealed record CommissionRefunded(string OrderId, string ChargeId, int AmountCents) : BillingEvent;
public sealed record AccountUpdated(string? ArtistId, string StripeAccountId, string? Email, bool DetailsSubmitted, bool ChargesEnabled, bool PayoutsEnabled) : BillingEvent;

public sealed record WebhookDelivery(string StripeEventId, string Type, BillingEvent? Event);

public enum DeliveryOutcome { Applied, Ignored, Duplicate }

public interface IBillingStore
{
    Task<SubscriptionRecord> RecordSubscriptionCheckoutAsync(SubscriptionCheckout checkout, CancellationToken cancellationToken = default);
    Task<CommissionRecord> RecordCommissionHoldAsync(CommissionHold hold, CancellationToken cancellationToken = default);
    Task<CommissionRecord> RecordCommissionReleaseAsync(CommissionRelease release, CancellationToken cancellationToken = default);
    Task<CommissionRecord> RecordCommissionRefundAsync(CommissionRefund refund, CancellationToken cancellationToken = default);
    Task<DeliveryOutcome> ApplyDeliveryAsync(string stripeEventId, string type, BillingEvent? billingEvent, CancellationToken cancellationToken = default);
    Task RecordHttpEventAsync(HttpEventDraft draft, CancellationToken cancellationToken = default);
    Task<SubscriptionRecord?> SubscriptionAsync(string businessAccountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionRecord>> SubscriptionsAsync(string businessAccountId, CancellationToken cancellationToken = default);
    Task<CommissionRecord?> CommissionAsync(string orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LedgerEntry>> LedgerAsync(string subjectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentModification>> ModificationsAsync(string paymentKind, string paymentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HttpEvent>> HttpEventsAsync(CancellationToken cancellationToken = default);
    Task<ConnectedAccountRecord?> ConnectedAccountAsync(string artistId, CancellationToken cancellationToken = default);
    Task<ConnectedAccountRecord> SaveConnectedAccountAsync(ConnectedAccountUpdate update, CancellationToken cancellationToken = default);
}
