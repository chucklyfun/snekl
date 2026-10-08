using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Snekl.Billing;

/// <summary>
/// Loads the billing book, applies one change, and writes it back.
/// A failed change is not saved, so a bad webhook does not consume its Stripe event id.
/// </summary>
public sealed class EfBillingStore : IBillingStore
{
    readonly IDbContextFactory<BillingDbContext> _factory;

    public EfBillingStore(IDbContextFactory<BillingDbContext> factory) => _factory = factory;

    public Task<SubscriptionRecord> RecordSubscriptionCheckoutAsync(SubscriptionCheckout checkout, CancellationToken cancellationToken = default) =>
        ChangeAsync(state => state.RecordSubscriptionCheckout(checkout), cancellationToken);

    public Task<CommissionRecord> RecordCommissionHoldAsync(CommissionHold hold, CancellationToken cancellationToken = default) =>
        ChangeAsync(state => state.RecordCommissionHold(hold), cancellationToken);

    public Task<CommissionRecord> RecordCommissionReleaseAsync(CommissionRelease release, CancellationToken cancellationToken = default) =>
        ChangeAsync(state => state.RecordCommissionRelease(release), cancellationToken);

    public Task<CommissionRecord> RecordCommissionRefundAsync(CommissionRefund refund, CancellationToken cancellationToken = default) =>
        ChangeAsync(state => state.RecordCommissionRefund(refund), cancellationToken);

    public Task<DeliveryOutcome> ApplyDeliveryAsync(string stripeEventId, string type, BillingEvent? billingEvent, CancellationToken cancellationToken = default) =>
        ChangeAsync(state => state.ApplyDelivery(stripeEventId, type, billingEvent), cancellationToken);

    public async Task RecordHttpEventAsync(HttpEventDraft draft, CancellationToken cancellationToken = default) =>
        await ChangeAsync(state => { state.RecordHttp(draft); return true; }, cancellationToken);

    public async Task<SubscriptionRecord?> SubscriptionAsync(string businessAccountId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var state = await LoadAsync(db, cancellationToken);
        return state.LatestSubscription(businessAccountId);
    }

    public async Task<IReadOnlyList<SubscriptionRecord>> SubscriptionsAsync(string businessAccountId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var state = await LoadAsync(db, cancellationToken);
        return state.Subscriptions.Where(row => row.BusinessAccountId == businessAccountId).OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).ToList();
    }

    public async Task<CommissionRecord?> CommissionAsync(string orderId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var state = await LoadAsync(db, cancellationToken);
        return state.Commissions.FirstOrDefault(row => row.OrderId == orderId);
    }

    public async Task<IReadOnlyList<LedgerEntry>> LedgerAsync(string subjectId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var state = await LoadAsync(db, cancellationToken);
        return state.Ledger.Where(row => row.SubjectId == subjectId).ToList();
    }

    public async Task<IReadOnlyList<PaymentModification>> ModificationsAsync(string paymentKind, string paymentId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var state = await LoadAsync(db, cancellationToken);
        return state.Modifications.Where(row => row.PaymentKind == paymentKind && row.PaymentId == paymentId).ToList();
    }

    public Task<ConnectedAccountRecord?> ConnectedAccountAsync(string artistId, CancellationToken cancellationToken = default) =>
        ReadAsync(state => state.ConnectedAccounts.FirstOrDefault(row => row.ArtistId == artistId), cancellationToken);

    public Task<ConnectedAccountRecord> SaveConnectedAccountAsync(ConnectedAccountUpdate update, CancellationToken cancellationToken = default) =>
        ChangeAsync(state => state.SaveConnectedAccount(update), cancellationToken);

    public async Task<IReadOnlyList<HttpEvent>> HttpEventsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var state = await LoadAsync(db, cancellationToken);
        return state.HttpEvents.ToList();
    }

    async Task<T> ReadAsync<T>(Func<BillingState, T> read, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return read(await LoadAsync(db, cancellationToken));
    }

    async Task<T> ChangeAsync<T>(Func<BillingState, T> change, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var state = await LoadAsync(db, cancellationToken);
        var result = change(state);
        await SaveAsync(db, state, cancellationToken);
        return result;
    }

    static async Task<BillingState> LoadAsync(BillingDbContext db, CancellationToken cancellationToken) =>
        BillingRows.ToState(
            await db.Subscriptions.AsNoTracking().ToListAsync(cancellationToken),
            await db.Commissions.AsNoTracking().ToListAsync(cancellationToken),
            await db.Ledger.AsNoTracking().OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).ToListAsync(cancellationToken),
            await db.StripeEvents.AsNoTracking().ToListAsync(cancellationToken),
            await db.Modifications.AsNoTracking().ToListAsync(cancellationToken),
            await db.HttpEvents.AsNoTracking().ToListAsync(cancellationToken),
            await db.ConnectedAccounts.AsNoTracking().ToListAsync(cancellationToken));

    static async Task SaveAsync(BillingDbContext db, BillingState state, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Subscriptions.ExecuteDeleteAsync(cancellationToken);
        await db.Commissions.ExecuteDeleteAsync(cancellationToken);
        await db.Ledger.ExecuteDeleteAsync(cancellationToken);
        await db.StripeEvents.ExecuteDeleteAsync(cancellationToken);
        await db.Modifications.ExecuteDeleteAsync(cancellationToken);
        await db.HttpEvents.ExecuteDeleteAsync(cancellationToken);
        await db.ConnectedAccounts.ExecuteDeleteAsync(cancellationToken);
        db.ChangeTracker.Clear();

        db.Subscriptions.AddRange(state.Subscriptions.Select(BillingRows.ToRow));
        db.Commissions.AddRange(state.Commissions.Select(BillingRows.ToRow));
        db.Ledger.AddRange(state.Ledger.Select(entry => new BillingLedgerRow
        {
            Id = entry.Id,
            StripeEventId = entry.StripeEventId,
            Kind = entry.Kind,
            SubjectId = entry.SubjectId,
            AmountCents = entry.AmountCents,
            DetailJson = entry.Detail,
            CreatedAt = entry.CreatedAt,
        }));
        db.StripeEvents.AddRange(state.StripeEvents.Select(entry => new BillingStripeEventRow
        {
            StripeEventId = entry.Id,
            EventType = entry.Type,
            Outcome = entry.Outcome,
            ReceivedAt = entry.At,
        }));
        for (var i = 0; i < state.Modifications.Count; i++)
        {
            var entry = state.Modifications[i];
            db.Modifications.Add(new BillingModificationRow
            {
                Seq = i + 1,
                Id = entry.Id,
                PaymentKind = entry.PaymentKind,
                PaymentId = entry.PaymentId,
                Cause = entry.Cause,
                Action = entry.Action,
                StripeEventId = entry.StripeEventId,
                BeforeJson = BillingRows.WriteElement(entry.Before),
                AfterJson = entry.After.GetRawText(),
                ChangesJson = JsonSerializer.Serialize(entry.Changes, BillingJson.Options),
                CreatedAt = entry.CreatedAt,
            });
        }
        db.ConnectedAccounts.AddRange(state.ConnectedAccounts.Select(BillingRows.ToRow));
        for (var i = 0; i < state.HttpEvents.Count; i++)
        {
            var entry = state.HttpEvents[i];
            db.HttpEvents.Add(new BillingHttpEventRow
            {
                Seq = i + 1,
                Id = entry.Id,
                Direction = entry.Direction,
                Method = entry.Method,
                Path = entry.Path,
                RequestHeadersJson = JsonSerializer.Serialize(entry.RequestHeaders, BillingJson.Options),
                RequestBody = entry.RequestBody,
                ResponseStatus = entry.ResponseStatus,
                ResponseHeadersJson = JsonSerializer.Serialize(entry.ResponseHeaders, BillingJson.Options),
                ResponseBody = entry.ResponseBody,
                Problem = entry.Problem,
                CreatedAt = entry.CreatedAt,
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
