using Snekl.Commissions.Payments;

namespace Snekl.Commissions.Tests;

public class CcBillDigestTests
{
    [Fact]
    public void FormDigest_MatchesCcBillDocVector()
    {
        // https://ccbill.com/doc/formdigest-value
        // initialPrice=10.00, initialPeriod=30, currencyCode=840, salt=7d901dad245fd0ff6bc20d06
        var digest = CcBillDigest.FormDigest(1000, "7d901dad245fd0ff6bc20d06", initialPeriodDays: 30);
        Assert.Equal("a7459445d0e5dc0963fe736dc5cf900b", digest);
    }

    [Fact]
    public void FormDigest_UsesTwoDayPeriodByDefault()
    {
        var digest = CcBillDigest.FormDigest(2000, "7d901dad245fd0ff6bc20d06");
        Assert.Equal(32, digest.Length);
        Assert.True(CcBillDigest.FormDigestMatches(2000, "7d901dad245fd0ff6bc20d06", digest));
        Assert.False(CcBillDigest.FormDigestMatches(2000, "7d901dad245fd0ff6bc20d06", "deadbeef"));
    }

    [Fact]
    public void ParsePriceToCents_ReadsCustomerPrice()
    {
        Assert.Equal(1840, CcBillDigest.ParsePriceToCents("18.40"));
        Assert.Equal(1000, CcBillDigest.ParsePriceToCents("10.00"));
    }
}
