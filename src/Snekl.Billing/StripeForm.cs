namespace Snekl.Billing;

public static class StripeForm
{
    public static string Encode(IReadOnlyDictionary<string, object?> value)
    {
        var parts = new List<(string Key, string Value)>();
        Append(parts, "", value);
        return string.Join("&", parts.Select(part => $"{Uri.EscapeDataString(part.Key)}={Uri.EscapeDataString(part.Value)}"));
    }

    static void Append(List<(string Key, string Value)> parts, string prefix, object? value)
    {
        if (value is null) return;
        if (value is IReadOnlyList<object?> list)
        {
            for (var i = 0; i < list.Count; i++) Append(parts, $"{prefix}[{i}]", list[i]);
            return;
        }
        if (value is IReadOnlyDictionary<string, object?> map)
        {
            foreach (var (key, child) in map) Append(parts, prefix.Length == 0 ? key : $"{prefix}[{key}]", child);
            return;
        }
        parts.Add((prefix, value switch
        {
            bool flag => flag ? "true" : "false",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
        }));
    }
}

public static class StripeRequests
{
    public const string ApiVersion = "2024-11-20.acacia";

    public static (IReadOnlyDictionary<string, object?> Body, IReadOnlyList<BusinessPlan> Plans) SubscriptionCheckout(string businessAccountId, IReadOnlyList<string> serviceKeys, string customerEmail, string successUrl, string cancelUrl)
    {
        var accountId = Required(businessAccountId, "A business account is required.");
        var email = customerEmail.Trim();
        if (!email.Contains('@')) throw new BillingException("invalid_email", "A contact email is required.");
        AssertHttpUrl(successUrl, "success");
        AssertHttpUrl(cancelUrl, "cancel");
        var plans = BillingCatalog.QuoteSubscription(serviceKeys);
        var services = string.Join(",", plans.Select(plan => plan.Key));
        var metadata = new Dictionary<string, object?>
        {
            [StripeWebhook.MetadataKind] = StripeWebhook.KindBusinessSubscription,
            ["business_account_id"] = accountId,
            ["services"] = services,
        };
        IReadOnlyDictionary<string, object?> body = new Dictionary<string, object?>
        {
            ["mode"] = "subscription",
            ["customer_email"] = email,
            ["success_url"] = successUrl,
            ["cancel_url"] = cancelUrl,
            ["client_reference_id"] = accountId,
            ["metadata"] = metadata,
            ["subscription_data"] = new Dictionary<string, object?> { ["metadata"] = metadata },
            ["line_items"] = plans.Select(plan => (object?)new Dictionary<string, object?>
            {
                ["quantity"] = 1,
                ["price_data"] = new Dictionary<string, object?>
                {
                    ["currency"] = plan.Currency,
                    ["unit_amount"] = plan.UnitAmountCents,
                    ["recurring"] = new Dictionary<string, object?> { ["interval"] = plan.Interval },
                    ["product_data"] = new Dictionary<string, object?>
                    {
                        ["name"] = plan.Label,
                        ["description"] = plan.Description,
                        ["metadata"] = new Dictionary<string, object?>
                        {
                            ["snekl_service"] = plan.Key,
                            ["snekl_lookup_key"] = plan.LookupKey,
                        },
                    },
                },
            }).ToList(),
        };
        return (body, plans);
    }

    public static (IReadOnlyDictionary<string, object?> Body, CommissionQuote Quote) CommissionHold(string orderId, int amountCents, string artistAccountId, string? currency)
    {
        var id = Required(orderId, "A commission order is required.");
        var artist = RequiredAccount(artistAccountId);
        var quote = BillingCatalog.QuoteCommission(amountCents, currency ?? "usd");
        IReadOnlyDictionary<string, object?> body = new Dictionary<string, object?>
        {
            ["amount"] = quote.AmountCents,
            ["currency"] = quote.Currency,
            ["transfer_group"] = id,
            ["automatic_payment_methods"] = new Dictionary<string, object?> { ["enabled"] = true },
            ["metadata"] = CommissionMetadata(id, artist, quote),
        };
        return (body, quote);
    }

    public static (IReadOnlyDictionary<string, object?> Body, CommissionQuote Quote) CommissionRelease(CommissionRelease release)
    {
        var orderId = Required(release.OrderId, "A commission order is required.");
        var chargeId = Required(release.ChargeId, "The held charge is required.");
        var artist = RequiredAccount(release.ArtistAccountId);
        var quote = BillingCatalog.SplitCommission(release.AmountCents, release.PlatformFeeCents);
        IReadOnlyDictionary<string, object?> body = new Dictionary<string, object?>
        {
            ["amount"] = quote.ArtistCents,
            ["currency"] = quote.Currency,
            ["destination"] = artist,
            ["source_transaction"] = chargeId,
            ["transfer_group"] = orderId,
            ["metadata"] = new Dictionary<string, object?>
            {
                [StripeWebhook.MetadataKind] = StripeWebhook.KindCommissionRelease,
                ["order_id"] = orderId,
                ["artist_account_id"] = artist,
                ["platform_fee_cents"] = quote.PlatformFeeCents.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["artist_cents"] = quote.ArtistCents.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
        };
        return (body, quote);
    }

    public static IReadOnlyDictionary<string, object?> CommissionRefund(string orderId, string paymentIntentId)
    {
        var id = Required(orderId, "A commission order is required.");
        var payment = Required(paymentIntentId, "The held payment is required.");
        return new Dictionary<string, object?>
        {
            ["payment_intent"] = payment,
            ["metadata"] = new Dictionary<string, object?>
            {
                [StripeWebhook.MetadataKind] = StripeWebhook.KindCommissionEscrow,
                ["order_id"] = id,
            },
        };
    }

    public static IReadOnlyDictionary<string, object?> StandardAccount(string artistId, string? email)
    {
        var body = new Dictionary<string, object?>
        {
            ["type"] = "standard",
            ["capabilities"] = new Dictionary<string, object?>
            {
                ["transfers"] = new Dictionary<string, object?> { ["requested"] = true },
            },
            ["metadata"] = new Dictionary<string, object?> { ["snekl_artist_id"] = artistId },
        };
        if (!string.IsNullOrWhiteSpace(email)) body["email"] = email.Trim();
        return body;
    }

    public static IReadOnlyDictionary<string, object?> AccountLink(string accountId, string returnUrl, string refreshUrl) => new Dictionary<string, object?>
    {
        ["account"] = accountId,
        ["refresh_url"] = refreshUrl,
        ["return_url"] = returnUrl,
        ["type"] = "account_onboarding",
    };

    public static IReadOnlyDictionary<string, object?> OAuthToken(string code) => new Dictionary<string, object?>
    {
        ["grant_type"] = "authorization_code",
        ["code"] = code,
    };

    public static string OAuthAuthorizeUrl(string clientId, string redirectUri, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["scope"] = "read_write",
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
        };
        return "https://connect.stripe.com/oauth/authorize?" + string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    static Dictionary<string, object?> CommissionMetadata(string orderId, string artistAccountId, CommissionQuote quote) => new()
    {
        [StripeWebhook.MetadataKind] = StripeWebhook.KindCommissionEscrow,
        ["order_id"] = orderId,
        ["artist_account_id"] = artistAccountId,
        ["platform_fee_cents"] = quote.PlatformFeeCents.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["artist_cents"] = quote.ArtistCents.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    static string Required(string value, string message)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) throw new BillingException("invalid_id", message);
        return trimmed;
    }

    static string RequiredAccount(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("acct_", StringComparison.Ordinal))
            throw new BillingException("invalid_account", "The artist needs a Stripe account before escrow can be held.");
        return trimmed;
    }

    public static void AssertReturnUrls(string returnUrl, string refreshUrl)
    {
        AssertHttpUrl(returnUrl, "return");
        AssertHttpUrl(refreshUrl, "refresh");
    }

    public static void AssertHttpUrl(string value, string which)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
            throw new BillingException("invalid_url", $"The {which} URL is not valid.");
    }
}
