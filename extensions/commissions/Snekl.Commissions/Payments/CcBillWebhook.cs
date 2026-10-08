using Snekl.Commissions.Data;
using Snekl.Commissions.Workflow;

namespace Snekl.Commissions.Payments;

public sealed record CcBillWebhookPayload(
    string EventType,
    string? TransactionId,
    string? ClientAcc,
    string? Subacc,
    string? ChargedPrice,
    string? ResponseDigest,
    Guid? OrderId,
    Guid? MilestoneId);

public enum CcBillWebhookOutcome
{
    Applied,
    Duplicate,
    Ignored,
    Rejected
}

public static class CcBillWebhook
{
    public static readonly HashSet<string> SaleSuccess = new(StringComparer.OrdinalIgnoreCase)
    {
        "NewSaleSuccess",
        "newsalesuccess"
    };

    public static readonly HashSet<string> RefundEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "Refund",
        "refund",
        "Cancellation",
        "cancellation"
    };

    public static readonly HashSet<string> ChargebackEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "Chargeback",
        "chargeback",
        "UserReimbursement",
        "userreimbursement"
    };

    public static CcBillWebhookPayload Parse(IReadOnlyDictionary<string, string> form)
    {
        string? Get(params string[] keys)
        {
            foreach (var key in keys)
            {
                if (form.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return null;
        }

        var eventType = Get("eventType", "event", "subscriptionTypeId") ?? "NewSaleSuccess";
        var orderRaw = Get("snekl_order_id", "order_id");
        var milestoneRaw = Get("snekl_milestone_id", "milestone_id");
        Guid? orderId = Guid.TryParse(orderRaw, out var o) ? o : null;
        Guid? milestoneId = Guid.TryParse(milestoneRaw, out var m) ? m : null;

        return new CcBillWebhookPayload(
            eventType,
            Get("transactionId", "subscriptionId", "clientTransId"),
            Get("clientAccnum", "clientAccountNumber"),
            Get("clientSubacc", "clientSubaccount"),
            Get("billedInitialPrice", "initialPrice", "accountingAmount"),
            Get("dynamicPricingValidationDigest", "responseDigest", "formDigest"),
            orderId,
            milestoneId);
    }

    public static (CcBillWebhookOutcome Outcome, string Message) Apply(
        CcBillWebhookPayload payload,
        ArtistStudio studio,
        CommissionOrder order,
        CommissionMilestone milestone,
        bool alreadySeenTransaction)
    {
        if (alreadySeenTransaction)
            return (CcBillWebhookOutcome.Duplicate, "Already processed.");

        if (string.IsNullOrWhiteSpace(payload.TransactionId))
            return (CcBillWebhookOutcome.Rejected, "Missing transaction id.");

        if (!string.Equals(studio.CcBillClientAcc, payload.ClientAcc, StringComparison.Ordinal)
            || !string.Equals(studio.CcBillSubacc, payload.Subacc, StringComparison.Ordinal))
            return (CcBillWebhookOutcome.Rejected, "CCBill account does not match the studio.");

        if (order.Id != payload.OrderId || milestone.OrderId != order.Id || milestone.Id != payload.MilestoneId)
            return (CcBillWebhookOutcome.Rejected, "Order or milestone id does not match.");

        if (order.PaymentMethod != CommissionModes.CcBill)
            return (CcBillWebhookOutcome.Rejected, "Order is not a CCBill commission.");

        if (string.IsNullOrWhiteSpace(studio.CcBillSalt))
            return (CcBillWebhookOutcome.Rejected, "Studio has no CCBill salt.");

        int chargedCents;
        try
        {
            chargedCents = CcBillDigest.ParsePriceToCents(payload.ChargedPrice);
        }
        catch (InvalidOperationException ex)
        {
            return (CcBillWebhookOutcome.Rejected, ex.Message);
        }

        if (chargedCents != milestone.AmountCents)
            return (CcBillWebhookOutcome.Rejected, "Charged amount does not match the milestone.");

        if (SaleSuccess.Contains(payload.EventType))
        {
            if (string.IsNullOrWhiteSpace(payload.ResponseDigest)
                || !CcBillDigest.ResponseDigestMatches(chargedCents, studio.CcBillSalt, payload.ResponseDigest))
                return (CcBillWebhookOutcome.Rejected, "Response digest is invalid.");

            if (milestone.Status == MilestoneStatuses.Paid
                && string.Equals(milestone.CcBillTransactionId, payload.TransactionId, StringComparison.Ordinal))
                return (CcBillWebhookOutcome.Duplicate, "Milestone already paid.");

            if (milestone.Status != MilestoneStatuses.Pending)
                return (CcBillWebhookOutcome.Rejected, "Milestone is not pending.");

            var gate = MilestoneRules.PayGate(order, milestone);
            if (gate is not null)
                return (CcBillWebhookOutcome.Rejected, gate);

            milestone.Status = MilestoneStatuses.Paid;
            milestone.CcBillTransactionId = payload.TransactionId;
            milestone.PaidAt = DateTime.UtcNow;
            milestone.UpdatedAt = DateTime.UtcNow;
            return (CcBillWebhookOutcome.Applied, "Milestone marked paid.");
        }

        if (RefundEvents.Contains(payload.EventType))
        {
            if (milestone.Status == MilestoneStatuses.Refunded)
                return (CcBillWebhookOutcome.Duplicate, "Already refunded.");
            if (milestone.Status is not MilestoneStatuses.Paid and not MilestoneStatuses.ChargedBack)
                return (CcBillWebhookOutcome.Rejected, "Only paid milestones can be refunded.");
            milestone.Status = MilestoneStatuses.Refunded;
            milestone.UpdatedAt = DateTime.UtcNow;
            return (CcBillWebhookOutcome.Applied, "Milestone marked refunded.");
        }

        if (ChargebackEvents.Contains(payload.EventType))
        {
            if (milestone.Status == MilestoneStatuses.ChargedBack)
                return (CcBillWebhookOutcome.Duplicate, "Already charged back.");
            if (milestone.Status is not MilestoneStatuses.Paid and not MilestoneStatuses.Refunded)
                return (CcBillWebhookOutcome.Rejected, "Only paid milestones can be charged back.");
            milestone.Status = MilestoneStatuses.ChargedBack;
            milestone.UpdatedAt = DateTime.UtcNow;
            return (CcBillWebhookOutcome.Applied, "Milestone marked charged back.");
        }

        return (CcBillWebhookOutcome.Ignored, "Unhandled event type.");
    }
}
