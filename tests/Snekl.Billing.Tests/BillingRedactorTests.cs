namespace Snekl.Billing.Tests;

public class BillingRedactorTests
{
    [Fact]
    public void KeepsTheFormBodyAndHidesCardNumbersAndSecrets()
    {
        var body = BillingRedactor.MaskBody(
            "amount=2000&card[number]=4242424242424242&card[cvc]=123&client_secret=pi_1_secret_abc&note=sk_live_supersecret",
            "application/x-www-form-urlencoded");
        Assert.Contains("amount=2000", body, StringComparison.Ordinal);
        Assert.Contains("4242", body, StringComparison.Ordinal);
        Assert.DoesNotContain("4242424242424242", body, StringComparison.Ordinal);
        Assert.DoesNotContain("pi_1_secret_abc", body, StringComparison.Ordinal);
        Assert.DoesNotContain("sk_live_supersecret", body, StringComparison.Ordinal);
        Assert.Contains("card%5Bnumber%5D=", body, StringComparison.Ordinal);
    }

    [Fact]
    public void MasksSecretsInsideAJsonResponse()
    {
        var body = BillingRedactor.MaskBody(
            """{"id":"pi_1","client_secret":"pi_1_secret_abc","card":{"number":"4242 4242 4242 4242"}}""",
            "application/json");
        Assert.NotNull(body);
        Assert.Contains("pi_1", body, StringComparison.Ordinal);
        Assert.Contains("\"client_secret\":\"***\"", body, StringComparison.Ordinal);
        Assert.Contains("\"number\":\"****4242\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("pi_1_secret_abc", body, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsUsefulHeadersAndMasksCredentials()
    {
        var headers = BillingRedactor.PickHeaders(
            new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer sk_test_123",
                ["Content-Type"] = "application/x-www-form-urlencoded",
                ["Idempotency-Key"] = "commission_hold_order_1",
                ["Stripe-Signature"] = "t=1,v1=abcdef",
                ["Cookie"] = "session=secret",
            },
            ["content-type", "idempotency-key", "authorization", "stripe-signature"]);
        Assert.Equal("application/x-www-form-urlencoded", headers["content-type"]);
        Assert.Equal("commission_hold_order_1", headers["idempotency-key"]);
        Assert.Equal("***", headers["authorization"]);
        Assert.Equal("***", headers["stripe-signature"]);
        Assert.Equal("Bearer ***", BillingRedactor.MaskString("Bearer sk_test_123"));
    }
}
