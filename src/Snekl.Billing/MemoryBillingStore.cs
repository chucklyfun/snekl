namespace Snekl.Billing;

public sealed class MemoryBillingStore : IBillingStore
{
    readonly BillingState _state = new();

    public Task<SubscriptionRecord> RecordSubscriptionCheckoutAsync(SubscriptionCheckout checkout, CancellationToken cancellationToken = default) =>
        Task.FromResult(_state.RecordSubscriptionCheckout(checkout));

    public Task<CommissionRecord> RecordCommissionHoldAsync(CommissionHold hold, CancellationToken cancellationToken = default) =>
        Task.FromResult(_state.RecordCommissionHold(hold));

    public Task<CommissionRecord> RecordCommissionReleaseAsync(CommissionRelease release, CancellationToken cancellationToken = default) =>
        Task.FromResult(_state.RecordCommissionRelease(release));

    public Task<CommissionRecord> RecordCommissionRefundAsync(CommissionRefund refund, CancellationToken cancellationToken = default) =>
        Task.FromResult(_state.RecordCommissionRefund(refund));

    public Task<DeliveryOutcome> ApplyDeliveryAsync(string stripeEventId, string type, BillingEvent? billingEvent, CancellationToken cancellationToken = default) =>
        Task.FromResult(_state.ApplyDelivery(stripeEventId, type, billingEvent));

    public Task RecordHttpEventAsync(HttpEventDraft draft, CancellationToken cancellationToken = default)
    {
        _state.RecordHttp(draft);
        return Task.CompletedTask;
    }

    public Task<SubscriptionRecord?> SubscriptionAsync(string businessAccountId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_state.LatestSubscription(businessAccountId));

    public Task<IReadOnlyList<SubscriptionRecord>> SubscriptionsAsync(string businessAccountId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SubscriptionRecord>>(_state.Subscriptions.Where(row => row.BusinessAccountId == businessAccountId).OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).ToList());

    public Task<CommissionRecord?> CommissionAsync(string orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_state.Commissions.FirstOrDefault(row => row.OrderId == orderId));

    public Task<IReadOnlyList<LedgerEntry>> LedgerAsync(string subjectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerEntry>>(_state.Ledger.Where(row => row.SubjectId == subjectId).ToList());

    public Task<IReadOnlyList<PaymentModification>> ModificationsAsync(string paymentKind, string paymentId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PaymentModification>>(_state.Modifications.Where(row => row.PaymentKind == paymentKind && row.PaymentId == paymentId).ToList());

    public Task<IReadOnlyList<HttpEvent>> HttpEventsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<HttpEvent>>(_state.HttpEvents.ToList());

    public Task<ConnectedAccountRecord?> ConnectedAccountAsync(string artistId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_state.ConnectedAccounts.FirstOrDefault(row => row.ArtistId == artistId));

    public Task<ConnectedAccountRecord> SaveConnectedAccountAsync(ConnectedAccountUpdate update, CancellationToken cancellationToken = default) =>
        Task.FromResult(_state.SaveConnectedAccount(update));
}
