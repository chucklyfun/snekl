using Snekl.Commissions.Data;

namespace Snekl.Commissions.Workflow;

public static class MilestoneRules
{
    public static string? SumError(int priceCents, IReadOnlyList<int> amounts)
    {
        if (priceCents <= 0)
            return "Set a price before milestones.";
        if (amounts.Count == 0)
            return null;
        if (amounts.Any(a => a <= 0))
            return "Each milestone must be greater than zero.";
        if (amounts.Sum() != priceCents)
            return "Milestone amounts must add up to the commission price.";
        return null;
    }

    public static IReadOnlyList<(string Label, int AmountCents)> DefaultMilestones(int priceCents) =>
        [("Full payment", priceCents)];

    public static string? ValidateDraft(int priceCents, IReadOnlyList<(string Label, int AmountCents)> draft)
    {
        if (draft.Count == 0)
            return null;
        if (draft.Any(m => string.IsNullOrWhiteSpace(m.Label)))
            return "Each milestone needs a label.";
        return SumError(priceCents, draft.Select(m => m.AmountCents).ToList());
    }

    public static bool FirstPaid(IEnumerable<CommissionMilestone> milestones)
    {
        var ordered = milestones.OrderBy(m => m.Sequence).ToList();
        if (ordered.Count == 0)
            return true;
        return ordered[0].Status == MilestoneStatuses.Paid;
    }

    public static bool AllPaid(IEnumerable<CommissionMilestone> milestones)
    {
        var list = milestones.ToList();
        if (list.Count == 0)
            return true;
        return list.All(m => m.Status == MilestoneStatuses.Paid);
    }

    public static bool AnyBad(IEnumerable<CommissionMilestone> milestones) =>
        milestones.Any(m => m.Status is MilestoneStatuses.Refunded or MilestoneStatuses.ChargedBack);

    public static CommissionMilestone? NextPayable(IEnumerable<CommissionMilestone> milestones)
    {
        var ordered = milestones.OrderBy(m => m.Sequence).ToList();
        if (ordered.Any(m => m.Status is MilestoneStatuses.Refunded or MilestoneStatuses.ChargedBack))
            return null;
        return ordered.FirstOrDefault(m => m.Status == MilestoneStatuses.Pending);
    }

    public static string? PayGate(CommissionOrder order, CommissionMilestone milestone)
    {
        if (order.PaymentMethod != CommissionModes.CcBill)
            return "This commission is not paid with CCBill.";
        if (CommissionStatuses.IsTerminal(order.Status))
            return "This commission is already closed.";
        if (order.Status is not CommissionStatuses.Accepted
            and not CommissionStatuses.InProgress
            and not CommissionStatuses.Delivered)
            return "Pay milestones after the commission is accepted.";
        if (milestone.Status != MilestoneStatuses.Pending)
            return "That milestone is not open for payment.";
        var next = NextPayable(order.Milestones);
        if (next is null || next.Id != milestone.Id)
            return "Pay the earlier unpaid milestone first.";
        return null;
    }
}
