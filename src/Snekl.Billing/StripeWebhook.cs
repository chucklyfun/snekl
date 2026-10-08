using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Snekl.Billing;

public static class StripeWebhook
{
    public const string MetadataKind = "snekl_kind";
    public const string KindBusinessSubscription = "business_subscription";
    public const string KindCommissionEscrow = "commission_escrow";
    public const string KindCommissionRelease = "commission_release";

    const int ToleranceMs = 5 * 60 * 1000;

    static readonly HashSet<string> KnownTypes =
    [
        "checkout.session.completed",
        "customer.subscription.deleted",
        "invoice.payment_failed",
        "payment_intent.succeeded",
        "transfer.created",
        "charge.refunded",
        "account.updated",
    ];

    public static WebhookDelivery Read(string rawBody, string? signatureHeader, string webhookSecret, DateTimeOffset? now = null)
    {
        var parsed = Verify(rawBody, signatureHeader, webhookSecret, now ?? DateTimeOffset.UtcNow);
        var id = parsed["id"]?.GetValue<string>() ?? "";
        if (id.Length == 0) throw new BillingException("malformed_event", "Stripe sent an event with no id.");
        var type = RequiredString(parsed["type"], "type");
        return new WebhookDelivery(id, type, Interpret(type, ObjectOf(parsed)));
    }

    public static BillingEvent? Interpret(string type, JsonObject? stripeObject)
    {
        if (!KnownTypes.Contains(type)) return null;
        if (stripeObject is null) throw new BillingException("malformed_event", "Stripe sent an event with no object.");
        return type switch
        {
            "checkout.session.completed" => SubscriptionStarted(stripeObject),
            "customer.subscription.deleted" => SubscriptionCanceled(stripeObject),
            "invoice.payment_failed" => SubscriptionPaymentFailed(stripeObject),
            "payment_intent.succeeded" => CommissionHeld(stripeObject),
            "transfer.created" => CommissionReleased(stripeObject),
            "charge.refunded" => CommissionRefunded(stripeObject),
            "account.updated" => AccountUpdated(stripeObject),
            _ => null,
        };
    }

    static JsonObject Verify(string rawBody, string? signatureHeader, string webhookSecret, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(signatureHeader)) throw new BillingException("bad_signature", "The Stripe signature is missing.");
        var parts = new Dictionary<string, List<string>>();
        foreach (var piece in signatureHeader.Split(','))
        {
            var split = piece.Split('=', 2);
            if (split.Length != 2 || split[0].Length == 0 || split[1].Length == 0) continue;
            if (!parts.TryGetValue(split[0], out var values)) parts[split[0]] = values = [];
            values.Add(split[1]);
        }
        if (!parts.TryGetValue("t", out var timestamps) || !parts.TryGetValue("v1", out var signatures) || timestamps.Count == 0 || signatures.Count == 0)
            throw new BillingException("bad_signature", "The Stripe signature is not valid.");
        if (!long.TryParse(timestamps[0], out var seconds)) throw new BillingException("bad_signature", "The Stripe signature is not valid.");
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (Math.Abs((now - timestamp).TotalMilliseconds) > ToleranceMs) throw new BillingException("bad_signature", "The Stripe signature is too old.");
        var expected = HmacHex(webhookSecret, $"{timestamps[0]}.{rawBody}");
        if (!signatures.Any(signature => FixedEquals(signature, expected)))
            throw new BillingException("bad_signature", "The Stripe signature does not match.");
        JsonNode? parsed;
        try { parsed = JsonNode.Parse(rawBody); }
        catch (System.Text.Json.JsonException) { throw new BillingException("malformed_event", "Stripe sent an event that is not JSON."); }
        if (parsed is not JsonObject root || parsed["type"] is not JsonValue) throw new BillingException("malformed_event", "Stripe sent an event with no type.");
        if (ObjectOf(root) is null) throw new BillingException("malformed_event", "Stripe sent an event with no object.");
        return root;
    }

    static BillingEvent? SubscriptionStarted(JsonObject stripeObject)
    {
        if (MetadataKindOf(stripeObject) != KindBusinessSubscription) return null;
        var services = RequiredMetadata(stripeObject, "services").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (services.Length == 0 || services.Any(key => !BillingCatalog.IsSubscriptionKey(key)))
            throw new BillingException("malformed_event", "The subscription is missing its services.");
        return new SubscriptionStarted(RequiredMetadata(stripeObject, "business_account_id"), services, RequiredId(stripeObject["subscription"], "subscription"), RequiredId(stripeObject["customer"], "customer"));
    }

    static BillingEvent? SubscriptionCanceled(JsonObject stripeObject)
    {
        if (MetadataKindOf(stripeObject) != KindBusinessSubscription) return null;
        return new SubscriptionCanceled(OptionalMetadata(stripeObject, "business_account_id"), RequiredId(stripeObject["id"], "subscription"));
    }

    static BillingEvent? SubscriptionPaymentFailed(JsonObject stripeObject)
    {
        var subscriptionId = IdOf(stripeObject["subscription"]);
        if (subscriptionId is null) return null;
        var details = SubscriptionDetails(stripeObject);
        var businessAccountId = OptionalMetadata(stripeObject, "business_account_id") ?? (details is null ? null : OptionalMetadata(details, "business_account_id"));
        var kind = MetadataKindOf(stripeObject) ?? (details is null ? null : MetadataKindOf(details));
        if (kind != KindBusinessSubscription && businessAccountId is null) return null;
        return new SubscriptionPaymentFailed(businessAccountId, subscriptionId);
    }

    static BillingEvent? CommissionHeld(JsonObject stripeObject)
    {
        if (MetadataKindOf(stripeObject) != KindCommissionEscrow) return null;
        var amount = RequiredCents(stripeObject["amount"], "amount");
        var fee = ParseCents(RequiredMetadata(stripeObject, "platform_fee_cents"));
        var artist = ParseCents(RequiredMetadata(stripeObject, "artist_cents"));
        if (fee + artist != amount) throw new BillingException("malformed_event", "The held commission does not add up to the charge.");
        return new CommissionHeld(RequiredMetadata(stripeObject, "order_id"), RequiredMetadata(stripeObject, "artist_account_id"), RequiredId(stripeObject["id"], "payment"), RequiredId(stripeObject["latest_charge"], "charge"), amount, fee, artist, RequiredString(stripeObject["currency"], "currency"));
    }

    static BillingEvent? CommissionReleased(JsonObject stripeObject)
    {
        if (MetadataKindOf(stripeObject) != KindCommissionRelease) return null;
        return new CommissionReleased(RequiredMetadata(stripeObject, "order_id"), RequiredMetadata(stripeObject, "artist_account_id"), RequiredId(stripeObject["id"], "transfer"), RequiredCents(stripeObject["amount"], "amount"), ParseCents(RequiredMetadata(stripeObject, "platform_fee_cents")));
    }

    static BillingEvent AccountUpdated(JsonObject stripeObject)
    {
        var accountId = RequiredId(stripeObject["id"], "account");
        if (!accountId.StartsWith("acct_", StringComparison.Ordinal))
            throw new BillingException("malformed_event", "The Stripe event is missing its account.");
        return new AccountUpdated(OptionalMetadata(stripeObject, "snekl_artist_id"), accountId, OptionalString(stripeObject["email"]), Flag(stripeObject["details_submitted"]), Flag(stripeObject["charges_enabled"]), Flag(stripeObject["payouts_enabled"]));
    }

    static BillingEvent? CommissionRefunded(JsonObject stripeObject)
    {
        if (MetadataKindOf(stripeObject) != KindCommissionEscrow) return null;
        var amount = RequiredCents(stripeObject["amount_refunded"], "amount_refunded");
        if (amount == 0) return null;
        return new CommissionRefunded(RequiredMetadata(stripeObject, "order_id"), RequiredId(stripeObject["id"], "charge"), amount);
    }

    static JsonObject? ObjectOf(JsonObject root) => root["data"]?["object"] as JsonObject;

    static JsonObject? SubscriptionDetails(JsonObject stripeObject)
    {
        if (stripeObject["subscription_details"] is JsonObject direct) return direct;
        if (stripeObject["parent"]?["subscription_details"] is JsonObject nested) return nested;
        return null;
    }

    static string? MetadataKindOf(JsonObject stripeObject) => OptionalMetadata(stripeObject, MetadataKind);

    static string? OptionalMetadata(JsonObject stripeObject, string key)
    {
        if (stripeObject["metadata"] is not JsonObject metadata) return null;
        var value = metadata[key]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    static string RequiredMetadata(JsonObject stripeObject, string key) =>
        OptionalMetadata(stripeObject, key) ?? throw new BillingException("malformed_event", $"The Stripe event is missing {key}.");

    static string? IdOf(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)) return text.Trim();
        if (node is JsonObject obj && obj["id"] is JsonValue id && id.TryGetValue<string>(out var nested) && !string.IsNullOrWhiteSpace(nested)) return nested.Trim();
        return null;
    }

    static string RequiredId(JsonNode? node, string label) => IdOf(node) ?? throw new BillingException("malformed_event", $"The Stripe event is missing its {label}.");

    static string RequiredString(JsonNode? node, string label)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)) return text.Trim();
        throw new BillingException("malformed_event", $"The Stripe event is missing its {label}.");
    }

    static int RequiredCents(JsonNode? node, string label)
    {
        if (node is JsonValue value && value.TryGetValue<int>(out var cents) && cents >= 0) return cents;
        if (node is JsonValue number && number.TryGetValue<double>(out var precise) && precise >= 0 && precise == Math.Truncate(precise)) return (int)precise;
        throw new BillingException("malformed_event", $"The Stripe event is missing its {label}.");
    }

    static int ParseCents(string value) => int.TryParse(value, out var cents) ? cents : 0;

    static string? OptionalString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    static bool Flag(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    static string HmacHex(string secret, string payload)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    static bool FixedEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
