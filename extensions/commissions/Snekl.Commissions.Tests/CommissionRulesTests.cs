using Snekl.Commissions.Workflow;

namespace Snekl.Commissions.Tests;

public class CommissionRulesTests
{
    [Fact]
    public void Adult_RejectsEscrow_AllowsCcBillAndOffPlatform()
    {
        Assert.Equal(
            "Adult commissions cannot use escrow (Stripe). Use CCBill or off-platform.",
            CommissionRules.PaymentMethodError(adult: true, CommissionModes.Escrow, studioHasCcBill: true));
        Assert.Null(CommissionRules.PaymentMethodError(adult: true, CommissionModes.CcBill, studioHasCcBill: true));
        Assert.Null(CommissionRules.PaymentMethodError(adult: true, CommissionModes.OffPlatform, studioHasCcBill: false));
        Assert.Equal(
            "The artist has not connected a CCBill account yet.",
            CommissionRules.PaymentMethodError(adult: true, CommissionModes.CcBill, studioHasCcBill: false));
    }

    [Fact]
    public void NonAdult_RejectsCcBill_AllowsEscrow()
    {
        Assert.Equal(
            "CCBill is only for adult listings.",
            CommissionRules.PaymentMethodError(adult: false, CommissionModes.CcBill, studioHasCcBill: true));
        Assert.Null(CommissionRules.PaymentMethodError(adult: false, CommissionModes.Escrow, studioHasCcBill: false));
    }

    [Fact]
    public void Escrow_HeldOnlyAfterAccept_ReleasedOnComplete_RefundedOnCancel()
    {
        Assert.NotNull(CommissionRules.EscrowError(
            CommissionModes.Escrow,
            CommissionStatuses.Requested,
            CommissionModes.EscrowNone,
            CommissionModes.EscrowHeld));
        Assert.Null(CommissionRules.EscrowError(
            CommissionModes.Escrow,
            CommissionStatuses.Accepted,
            CommissionModes.EscrowNone,
            CommissionModes.EscrowHeld));
    }

    [Fact]
    public void Complete_RequiresEscrowHeld_ForEscrowOrders()
    {
        Assert.Equal(
            "Mark the escrow as held before completing.",
            CommissionRules.TransitionError(
                CommissionModes.Fixed,
                CommissionStatuses.Delivered,
                CommissionStatuses.Completed,
                actorIsArtist: false,
                CommissionModes.Escrow,
                CommissionModes.EscrowNone));
    }

    [Fact]
    public void CcBill_BlocksInProgressUntilFirstPaid_AndCompleteUntilAllPaid()
    {
        Assert.Equal(
            "The client must pay the first CCBill milestone before work starts.",
            CommissionRules.TransitionError(
                CommissionModes.Fixed,
                CommissionStatuses.Accepted,
                CommissionStatuses.InProgress,
                actorIsArtist: true,
                CommissionModes.CcBill,
                CommissionModes.EscrowNone,
                firstMilestonePaid: false));

        Assert.Equal(
            "Every CCBill milestone must be paid before completing.",
            CommissionRules.TransitionError(
                CommissionModes.Fixed,
                CommissionStatuses.Delivered,
                CommissionStatuses.Completed,
                actorIsArtist: false,
                CommissionModes.CcBill,
                CommissionModes.EscrowNone,
                firstMilestonePaid: true,
                allMilestonesPaid: false));

        Assert.Equal(
            "A milestone was refunded or charged back; this order cannot be completed.",
            CommissionRules.TransitionError(
                CommissionModes.Fixed,
                CommissionStatuses.Delivered,
                CommissionStatuses.Completed,
                actorIsArtist: false,
                CommissionModes.CcBill,
                CommissionModes.EscrowNone,
                firstMilestonePaid: true,
                allMilestonesPaid: true,
                anyMilestoneBad: true));
    }
}
