namespace Snekl.Billing;

/// <summary>
/// Prices charged for business subscriptions and commission escrow.
/// The landing page keeps a copy for display in snekl-landing/src/stripe/catalog.ts. Change both together.
/// </summary>
public static class BillingCatalog
{
    public const int CommissionPlatformFeeBps = 800;

    public static readonly IReadOnlyList<string> SubscriptionKeys = ["server-hosting", "managed-auth", "extensions", "support"];

    static readonly Dictionary<string, int> MonthlyCents = new()
    {
        ["server-hosting"] = 4900,
        ["managed-auth"] = 1900,
        ["extensions"] = 1500,
        ["support"] = 2900,
    };

    static readonly Dictionary<string, (string Label, string Description)> Copy = new()
    {
        ["server-hosting"] = ("Hosted Snekl server", "Your own Snekl server with forums, threads, and chat. We run it, update it, and back it up. Members sign in with the Snekl accounts they already have."),
        ["managed-auth"] = ("Managed sign-in", "A sign-in server under your name that joins the Snekl auth ring, so your members keep one identity across the network."),
        ["extensions"] = ("Extensions and bots", "Add extensions such as commissions and calendars, or work with us on a custom integration or bot for your community."),
        ["support"] = ("Priority support", "A direct line to the team for setup questions, moderation tools, and incidents."),
    };

    static readonly Dictionary<string, string> CustomServiceMessage = new()
    {
        ["migration"] = "Community migration is arranged with us, not billed as a subscription.",
        ["other"] = "Custom work is arranged with us, not billed as a subscription.",
    };

    public static bool IsSubscriptionKey(string key) => SubscriptionKeys.Contains(key);

    public static IReadOnlyList<BusinessPlan> ListPlans() =>
        SubscriptionKeys.Select(key => new BusinessPlan(
            key,
            Copy[key].Label,
            Copy[key].Description,
            MonthlyCents[key],
            $"snekl_business_{key.Replace("-", "_", StringComparison.Ordinal)}_monthly")).ToList();

    public static IReadOnlyList<BusinessPlan> QuoteSubscription(IReadOnlyList<string> serviceKeys)
    {
        if (serviceKeys.Count == 0) throw new BillingException("empty_selection", "Choose at least one service.");
        var wanted = new HashSet<string>();
        foreach (var key in serviceKeys)
        {
            if (IsSubscriptionKey(key))
            {
                wanted.Add(key);
                continue;
            }
            if (CustomServiceMessage.TryGetValue(key, out var custom))
                throw new BillingException("not_a_subscription", custom);
            throw new BillingException("unknown_service", "That service is not a Snekl business subscription.");
        }
        return ListPlans().Where(plan => wanted.Contains(plan.Key)).ToList();
    }

    public static int MonthlyTotalCents(IReadOnlyList<BusinessPlan> plans) => plans.Sum(plan => plan.UnitAmountCents);

    public static CommissionQuote QuoteCommission(int amountCents, string currency = "usd")
    {
        if (amountCents <= 0) throw new BillingException("invalid_amount", "Enter a commission price.");
        if (!string.Equals(currency, "usd", StringComparison.OrdinalIgnoreCase))
            throw new BillingException("invalid_currency", "Commissions escrow is billed in USD.");
        var fee = (int)Math.Round(amountCents * CommissionPlatformFeeBps / 10000m, MidpointRounding.AwayFromZero);
        return SplitCommission(amountCents, fee);
    }

    public static CommissionQuote SplitCommission(int amountCents, int platformFeeCents)
    {
        if (amountCents <= 0) throw new BillingException("invalid_amount", "Enter a commission price.");
        if (platformFeeCents < 0 || platformFeeCents >= amountCents)
            throw new BillingException("invalid_fee", "The platform fee has to be smaller than the commission price.");
        return new CommissionQuote(amountCents, "usd", platformFeeCents, amountCents - platformFeeCents);
    }
}

public sealed record BusinessPlan(string Key, string Label, string Description, int UnitAmountCents, string LookupKey)
{
    public string Interval { get; } = "month";
    public string Currency { get; } = "usd";
}

public sealed record CommissionQuote(int AmountCents, string Currency, int PlatformFeeCents, int ArtistCents);
