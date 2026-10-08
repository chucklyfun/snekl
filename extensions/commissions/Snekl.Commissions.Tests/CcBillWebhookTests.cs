using Snekl.Commissions.Data;
using Snekl.Commissions.Payments;
using Snekl.Commissions.Workflow;

namespace Snekl.Commissions.Tests;

public class CcBillWebhookTests
{
    static (ArtistStudio Studio, CommissionOrder Order, CommissionMilestone Milestone) Fixture(int amountCents = 1000)
    {
        var orderId = Guid.NewGuid();
        var milestoneId = Guid.NewGuid();
        var studio = new ArtistStudio
        {
            CcBillClientAcc = "900000",
            CcBillSubacc = "0000",
            CcBillFlexFormId = "687fa3e0-e60d-4466-88e2-181fa56dd6a9",
            CcBillSalt = "7d901dad245fd0ff6bc20d06"
        };
        var milestone = new CommissionMilestone
        {
            Id = milestoneId,
            OrderId = orderId,
            Sequence = 1,
            Label = "Sketch",
            AmountCents = amountCents,
            Status = MilestoneStatuses.Pending
        };
        var order = new CommissionOrder
        {
            Id = orderId,
            PaymentMethod = CommissionModes.CcBill,
            Status = CommissionStatuses.Accepted,
            PriceCents = amountCents,
            Milestones = { milestone }
        };
        return (studio, order, milestone);
    }

    [Fact]
    public void NewSaleSuccess_MarksPaid_WhenDigestAndAmountMatch()
    {
        var (studio, order, milestone) = Fixture(1000);
        var digest = CcBillDigest.FormDigest(1000, studio.CcBillSalt!, initialPeriodDays: 2);
        var payload = new CcBillWebhookPayload(
            "NewSaleSuccess",
            "txn_1",
            studio.CcBillClientAcc,
            studio.CcBillSubacc,
            "10.00",
            digest,
            order.Id,
            milestone.Id);

        var (outcome, _) = CcBillWebhook.Apply(payload, studio, order, milestone, alreadySeenTransaction: false);
        Assert.Equal(CcBillWebhookOutcome.Applied, outcome);
        Assert.Equal(MilestoneStatuses.Paid, milestone.Status);
        Assert.Equal("txn_1", milestone.CcBillTransactionId);
    }

    [Fact]
    public void Rejects_AmountMismatch()
    {
        var (studio, order, milestone) = Fixture(1000);
        var digest = CcBillDigest.FormDigest(900, studio.CcBillSalt!);
        var payload = new CcBillWebhookPayload(
            "NewSaleSuccess",
            "txn_2",
            studio.CcBillClientAcc,
            studio.CcBillSubacc,
            "9.00",
            digest,
            order.Id,
            milestone.Id);

        var (outcome, message) = CcBillWebhook.Apply(payload, studio, order, milestone, false);
        Assert.Equal(CcBillWebhookOutcome.Rejected, outcome);
        Assert.Contains("amount", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(MilestoneStatuses.Pending, milestone.Status);
    }

    [Fact]
    public void Rejects_MissingDigestOnSale()
    {
        var (studio, order, milestone) = Fixture(1000);
        var payload = new CcBillWebhookPayload(
            "NewSaleSuccess",
            "txn_3",
            studio.CcBillClientAcc,
            studio.CcBillSubacc,
            "10.00",
            null,
            order.Id,
            milestone.Id);
        var (outcome, message) = CcBillWebhook.Apply(payload, studio, order, milestone, false);
        Assert.Equal(CcBillWebhookOutcome.Rejected, outcome);
        Assert.Contains("digest", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_Transaction_IsNoOp()
    {
        var (studio, order, milestone) = Fixture(1000);
        milestone.Status = MilestoneStatuses.Paid;
        milestone.CcBillTransactionId = "txn_1";
        var payload = new CcBillWebhookPayload(
            "NewSaleSuccess",
            "txn_1",
            studio.CcBillClientAcc,
            studio.CcBillSubacc,
            "10.00",
            null,
            order.Id,
            milestone.Id);

        var (outcome, _) = CcBillWebhook.Apply(payload, studio, order, milestone, alreadySeenTransaction: true);
        Assert.Equal(CcBillWebhookOutcome.Duplicate, outcome);
    }

    [Fact]
    public void Refund_And_Chargeback_BlockCompletion()
    {
        var (studio, order, milestone) = Fixture(1000);
        milestone.Status = MilestoneStatuses.Paid;
        milestone.CcBillTransactionId = "txn_1";

        var refund = new CcBillWebhookPayload(
            "Refund", "txn_1", studio.CcBillClientAcc, studio.CcBillSubacc, "10.00", null, order.Id, milestone.Id);
        Assert.Equal(CcBillWebhookOutcome.Applied, CcBillWebhook.Apply(refund, studio, order, milestone, false).Outcome);
        Assert.Equal(MilestoneStatuses.Refunded, milestone.Status);
        Assert.True(MilestoneRules.AnyBad(order.Milestones));
    }

    [Fact]
    public void BuildUrl_IncludesDigestAndCustomIds()
    {
        var orderId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var milestoneId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var url = CcBillCheckout.BuildUrl(
            new CcBillCheckoutRequest(
                "687fa3e0-e60d-4466-88e2-181fa56dd6a9",
                "900000",
                "0000",
                1000,
                orderId,
                milestoneId,
                "1626321"),
            "7d901dad245fd0ff6bc20d06");

        Assert.Contains("formDigest=", url);
        Assert.Contains("snekl_order_id=11111111-1111-1111-1111-111111111111", url);
        Assert.Contains("snekl_milestone_id=22222222-2222-2222-2222-222222222222", url);
        Assert.Contains("ccbill_referer=1626321", url);
        Assert.Contains("initialPeriod=2", url);
    }
}
