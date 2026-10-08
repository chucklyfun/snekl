using Snekl.Commissions.Data;
using Snekl.Commissions.Workflow;

namespace Snekl.Commissions.Tests;

public class MilestoneRulesTests
{
    [Fact]
    public void Sum_MustMatchPrice()
    {
        Assert.Equal(
            "Milestone amounts must add up to the commission price.",
            MilestoneRules.SumError(1000, [600, 300]));
        Assert.Null(MilestoneRules.SumError(1000, [600, 400]));
        Assert.Null(MilestoneRules.SumError(1000, []));
    }

    [Fact]
    public void NextPayable_IsFirstPending_InSequence()
    {
        var milestones = new List<CommissionMilestone>
        {
            new() { Id = Guid.NewGuid(), Sequence = 1, Status = MilestoneStatuses.Paid, AmountCents = 500 },
            new() { Id = Guid.NewGuid(), Sequence = 2, Status = MilestoneStatuses.Pending, AmountCents = 500 },
        };
        var next = MilestoneRules.NextPayable(milestones);
        Assert.Equal(2, next!.Sequence);
        Assert.True(MilestoneRules.FirstPaid(milestones));
        Assert.False(MilestoneRules.AllPaid(milestones));
    }

    [Fact]
    public void PayGate_RequiresSequentialPendingMilestone()
    {
        var first = new CommissionMilestone { Id = Guid.NewGuid(), Sequence = 1, Status = MilestoneStatuses.Pending, AmountCents = 500 };
        var second = new CommissionMilestone { Id = Guid.NewGuid(), Sequence = 2, Status = MilestoneStatuses.Pending, AmountCents = 500 };
        var order = new CommissionOrder
        {
            PaymentMethod = CommissionModes.CcBill,
            Status = CommissionStatuses.Accepted,
            Milestones = { first, second }
        };
        Assert.Null(MilestoneRules.PayGate(order, first));
        Assert.Equal("Pay the earlier unpaid milestone first.", MilestoneRules.PayGate(order, second));
    }
}
