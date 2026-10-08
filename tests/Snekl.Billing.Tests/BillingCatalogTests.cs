namespace Snekl.Billing.Tests;

public class BillingCatalogTests
{
    [Fact]
    public void QuotePricesRecurringServicesAndSkipsDuplicates()
    {
        var plans = BillingCatalog.QuoteSubscription(["support", "server-hosting", "server-hosting"]);
        Assert.Equal(["server-hosting", "support"], plans.Select(plan => plan.Key));
        Assert.Equal(4900 + 2900, BillingCatalog.MonthlyTotalCents(plans));
        Assert.All(plans, plan => Assert.Equal(("month", "usd"), (plan.Interval, plan.Currency)));
    }

    [Fact]
    public void UsesTheLandingServiceNames()
    {
        var hosting = BillingCatalog.ListPlans().Single(plan => plan.Key == "server-hosting");
        Assert.Equal("Hosted Snekl server", hosting.Label);
        Assert.Equal(4900, hosting.UnitAmountCents);
    }

    [Fact]
    public void RefusesServicesThatAreNotSubscriptions()
    {
        Assert.Equal("not_a_subscription", Assert.Throws<BillingException>(() => BillingCatalog.QuoteSubscription(["migration"])).Code);
        Assert.Equal("not_a_subscription", Assert.Throws<BillingException>(() => BillingCatalog.QuoteSubscription(["other"])).Code);
        Assert.Throws<BillingException>(() => BillingCatalog.QuoteSubscription([]));
        Assert.Equal("unknown_service", Assert.Throws<BillingException>(() => BillingCatalog.QuoteSubscription(["not-a-service"])).Code);
    }

    [Fact]
    public void KeepsAnEightPercentFee()
    {
        Assert.Equal(800, BillingCatalog.CommissionPlatformFeeBps);
        Assert.Equal(new CommissionQuote(2000, "usd", 160, 1840), BillingCatalog.QuoteCommission(2000));
        Assert.Equal(1840, BillingCatalog.SplitCommission(2000, 160).ArtistCents);
        Assert.Equal("invalid_fee", Assert.Throws<BillingException>(() => BillingCatalog.SplitCommission(2000, 2000)).Code);
        Assert.Equal("invalid_amount", Assert.Throws<BillingException>(() => BillingCatalog.QuoteCommission(0)).Code);
        Assert.Equal("invalid_currency", Assert.Throws<BillingException>(() => BillingCatalog.QuoteCommission(2000, "eur")).Code);
    }
}
