using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Snekl.Billing;

public sealed class StripeBillingOptions
{
    public string SecretKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string? ConnectionString { get; set; }
    public bool DebugHttp { get; set; }
    /// <summary>Connect client id (ca_…). Required when an artist signs in to a Stripe account they already have.</summary>
    public string ConnectClientId { get; set; } = "";
}

/// <summary>
/// Stripe Checkout for business subscriptions, and Connect charges, transfers, and refunds for commission escrow.
/// The landing site does not call this. A host passes a secret key and an <see cref="IBillingStore"/>.
/// </summary>
public sealed class StripeBillingClient
{
    readonly string _secretKey;
    readonly string _webhookSecret;
    readonly IBillingStore _store;
    readonly HttpClient _http;
    readonly bool _debugHttp;
    readonly string? _connectClientId;

    public StripeBillingClient(IBillingStore store, HttpClient http, string secretKey, string webhookSecret, bool debugHttp = false, string? connectClientId = null)
    {
        if (!secretKey.StartsWith("sk_", StringComparison.Ordinal) && !secretKey.StartsWith("rk_", StringComparison.Ordinal))
            throw new BillingException("config", "Stripe needs a secret key.");
        if (!webhookSecret.StartsWith("whsec_", StringComparison.Ordinal))
            throw new BillingException("config", "Stripe needs a webhook secret.");
        if (connectClientId is not null && !connectClientId.StartsWith("ca_", StringComparison.Ordinal))
            throw new BillingException("config", "Stripe Connect needs a client id.");
        _store = store;
        _http = http;
        _secretKey = secretKey;
        _webhookSecret = webhookSecret;
        _debugHttp = debugHttp;
        _connectClientId = connectClientId;
    }

    public async Task<(string AccountId, string? Url)> BeginArtistOnboardingAsync(string artistId, string? email, string returnUrl, string refreshUrl, CancellationToken cancellationToken = default)
    {
        var id = RequireArtist(artistId);
        if (email is not null && email.Trim().Length > 0 && !email.Contains('@'))
            throw new BillingException("invalid_email", "A contact email is required.");
        StripeRequests.AssertReturnUrls(returnUrl, refreshUrl);
        var current = await _store.ConnectedAccountAsync(id, cancellationToken);
        var accountId = current?.StripeAccountId;
        if (accountId is null)
        {
            var created = await PostAsync("accounts", StripeRequests.StandardAccount(id, email), $"connect_account_{id}", cancellationToken);
            accountId = StringField(created, "id") ?? throw new BillingException("stripe_request", "Stripe did not return an account.");
            if (!accountId.StartsWith("acct_", StringComparison.Ordinal))
                throw new BillingException("stripe_request", "Stripe did not return an account.");
            await RememberAsync(accountId, () => _store.SaveConnectedAccountAsync(new ConnectedAccountUpdate(id, accountId, EmailOf(created), false, false, false), cancellationToken));
            current = await _store.ConnectedAccountAsync(id, cancellationToken);
        }
        if (current is { DetailsSubmitted: true, PayoutsEnabled: true })
            return (accountId, null);
        var link = await PostAsync("account_links", StripeRequests.AccountLink(accountId, returnUrl, refreshUrl), $"connect_link_{id}_{Guid.NewGuid():N}", cancellationToken);
        var url = StringField(link, "url") ?? throw new BillingException("stripe_request", "Stripe did not return an onboarding link.");
        return (accountId, url);
    }

    public string ArtistOAuthUrl(string redirectUri, string state)
    {
        if (string.IsNullOrEmpty(_connectClientId))
            throw new BillingException("config", "Stripe Connect needs a client id.");
        StripeRequests.AssertHttpUrl(redirectUri, "redirect");
        if (string.IsNullOrWhiteSpace(state))
            throw new BillingException("invalid_id", "OAuth state is required.");
        return StripeRequests.OAuthAuthorizeUrl(_connectClientId, redirectUri, state.Trim());
    }

    public async Task<ConnectedAccountRecord> CompleteArtistOAuthAsync(string artistId, string code, CancellationToken cancellationToken = default)
    {
        var id = RequireArtist(artistId);
        if (string.IsNullOrWhiteSpace(code))
            throw new BillingException("invalid_id", "Stripe did not return an authorization code.");
        var token = await PostAbsoluteAsync("https://connect.stripe.com/oauth/token", "/oauth/token", StripeRequests.OAuthToken(code.Trim()), $"connect_oauth_{Guid.NewGuid():N}", cancellationToken);
        var accountId = StringField(token, "stripe_user_id") ?? throw new BillingException("stripe_request", "Stripe did not return an account.");
        if (!accountId.StartsWith("acct_", StringComparison.Ordinal))
            throw new BillingException("stripe_request", "Stripe did not return an account.");
        return await RefreshAccountAsync(id, accountId, cancellationToken);
    }

    public async Task<ConnectedAccountRecord> RefreshArtistAccountAsync(string artistId, CancellationToken cancellationToken = default)
    {
        var id = RequireArtist(artistId);
        var current = await _store.ConnectedAccountAsync(id, cancellationToken)
            ?? throw new BillingException("not_connected", "This artist has not connected a Stripe account.");
        return await RefreshAccountAsync(id, current.StripeAccountId, cancellationToken);
    }

    public async Task<(string SessionId, string Url)> StartBusinessSubscriptionAsync(string businessAccountId, IReadOnlyList<string> serviceKeys, string customerEmail, string successUrl, string cancelUrl, CancellationToken cancellationToken = default)
    {
        var (body, plans) = StripeRequests.SubscriptionCheckout(businessAccountId, serviceKeys, customerEmail, successUrl, cancelUrl);
        var session = await PostAsync("checkout/sessions", body, $"business_sub_{businessAccountId.Trim()}", cancellationToken);
        var sessionId = StringField(session, "id");
        var url = StringField(session, "url");
        if (sessionId is null || url is null) throw new BillingException("stripe_request", "Stripe did not return a checkout link.");
        await RememberAsync(sessionId, () => _store.RecordSubscriptionCheckoutAsync(new SubscriptionCheckout(businessAccountId.Trim(), customerEmail.Trim(), plans.Select(plan => plan.Key).ToList(), BillingCatalog.MonthlyTotalCents(plans), sessionId), cancellationToken));
        return (sessionId, url);
    }

    public async Task<(string PaymentIntentId, string ClientSecret, int AmountCents, int PlatformFeeCents, int ArtistCents)> HoldCommissionAsync(string orderId, int amountCents, string artistAccountId, string currency = "usd", CancellationToken cancellationToken = default)
    {
        var (body, quote) = StripeRequests.CommissionHold(orderId, amountCents, artistAccountId, currency);
        var intent = await PostAsync("payment_intents", body, $"commission_hold_{orderId.Trim()}", cancellationToken);
        var paymentIntentId = StringField(intent, "id");
        var clientSecret = StringField(intent, "client_secret");
        if (paymentIntentId is null || clientSecret is null) throw new BillingException("stripe_request", "Stripe did not return a payment to confirm.");
        await RememberAsync(paymentIntentId, () => _store.RecordCommissionHoldAsync(new CommissionHold(orderId.Trim(), artistAccountId.Trim(), quote.AmountCents, quote.PlatformFeeCents, quote.ArtistCents, quote.Currency, paymentIntentId), cancellationToken));
        return (paymentIntentId, clientSecret, quote.AmountCents, quote.PlatformFeeCents, quote.ArtistCents);
    }

    public async Task<(string TransferId, int ArtistCents, int PlatformFeeCents)> ReleaseCommissionAsync(CommissionRelease release, CancellationToken cancellationToken = default)
    {
        var orderId = release.OrderId.Trim();
        var current = await _store.CommissionAsync(orderId, cancellationToken);
        var prepared = release with { OrderId = orderId, ArtistAccountId = release.ArtistAccountId.Trim(), ChargeId = release.ChargeId.Trim() };
        if (BillingState.ReleaseGate(current, prepared) == "done" && current?.StripeTransferId is not null)
            return (current.StripeTransferId, current.ArtistCents, current.PlatformFeeCents);
        var (body, quote) = StripeRequests.CommissionRelease(prepared);
        var transfer = await PostAsync("transfers", body, $"commission_release_{orderId}", cancellationToken);
        var transferId = StringField(transfer, "id") ?? throw new BillingException("stripe_request", "Stripe did not return a transfer.");
        await RememberAsync(transferId, () => _store.RecordCommissionReleaseAsync(prepared with { StripeTransferId = transferId }, cancellationToken));
        return (transferId, quote.ArtistCents, quote.PlatformFeeCents);
    }

    public async Task<string> RefundCommissionAsync(string orderId, string paymentIntentId, CancellationToken cancellationToken = default)
    {
        var id = orderId.Trim();
        var current = await _store.CommissionAsync(id, cancellationToken);
        if (BillingState.RefundGate(current) == "done")
            return current?.StripeRefundId ?? throw new BillingException("already_closed", "That commission escrow is already closed.");
        var refund = await PostAsync("refunds", StripeRequests.CommissionRefund(id, paymentIntentId), $"commission_refund_{id}", cancellationToken);
        var refundId = StringField(refund, "id") ?? throw new BillingException("stripe_request", "Stripe did not return a refund.");
        await RememberAsync(refundId, () => _store.RecordCommissionRefundAsync(new CommissionRefund(id, refundId), cancellationToken));
        return refundId;
    }

    public async Task<BillingEvent?> ReceiveWebhookAsync(string rawBody, string? signatureHeader, DateTimeOffset? now = null, CancellationToken cancellationToken = default)
    {
        var requestHeaders = BillingRedactor.PickHeaders(
            new Dictionary<string, string> { ["content-type"] = "application/json", ["stripe-signature"] = signatureHeader ?? "" },
            BillingRedactor.RequestHeaderNames);
        try
        {
            var delivery = StripeWebhook.Read(rawBody, signatureHeader, _webhookSecret, now);
            var outcome = await _store.ApplyDeliveryAsync(delivery.StripeEventId, delivery.Type, delivery.Event, cancellationToken);
            await LogAsync(new HttpEventDraft("inbound", "POST", "/webhook", requestHeaders, BillingRedactor.MaskBody(rawBody, "application/json"), 200, new Dictionary<string, string>(), null, null), false, cancellationToken);
            return outcome is DeliveryOutcome.Duplicate or DeliveryOutcome.Ignored ? null : delivery.Event;
        }
        catch (Exception error)
        {
            await LogAsync(new HttpEventDraft("inbound", "POST", "/webhook", requestHeaders, BillingRedactor.MaskBody(rawBody, "application/json"), null, new Dictionary<string, string>(), null, error.Message), true, cancellationToken);
            throw;
        }
    }

    async Task RememberAsync(string stripeId, Func<Task> save)
    {
        try { await save(); }
        catch (Exception error)
        {
            var code = error is BillingException billing ? billing.Code : "record";
            throw new BillingException(code, $"{error.Message} Stripe id: {stripeId}.");
        }
    }

    async Task<JsonObject> PostAsync(string path, IReadOnlyDictionary<string, object?> body, string idempotencyKey, CancellationToken cancellationToken) =>
        await PostAbsoluteAsync($"https://api.stripe.com/v1/{path}", $"/v1/{path}", body, idempotencyKey, cancellationToken);

    async Task<ConnectedAccountRecord> RefreshAccountAsync(string artistId, string accountId, CancellationToken cancellationToken)
    {
        var account = await GetAsync($"accounts/{accountId}", cancellationToken);
        var saved = await _store.SaveConnectedAccountAsync(new ConnectedAccountUpdate(artistId, accountId, EmailOf(account), Flag(account, "details_submitted"), Flag(account, "charges_enabled"), Flag(account, "payouts_enabled")), cancellationToken);
        return saved;
    }

    static string RequireArtist(string artistId)
    {
        var id = artistId.Trim();
        if (id.Length == 0) throw new BillingException("invalid_id", "An artist is required.");
        return id;
    }

    static string? EmailOf(JsonObject body) => StringField(body, "email");

    static bool Flag(JsonObject body, string key) => body[key] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    async Task<JsonObject> GetAsync(string path, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>
        {
            ["authorization"] = $"Bearer {_secretKey}",
            ["stripe-version"] = StripeRequests.ApiVersion,
        };
        var loggedHeaders = BillingRedactor.PickHeaders(headers, BillingRedactor.RequestHeaderNames);
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.stripe.com/v1/{path}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secretKey);
            request.Headers.TryAddWithoutValidation("Stripe-Version", StripeRequests.ApiVersion);
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            await LogAsync(new HttpEventDraft("outbound", "GET", $"/v1/{path}", loggedHeaders, null, null, new Dictionary<string, string>(), null, "Stripe could not be reached."), true, cancellationToken);
            throw new BillingException("stripe_request", "Stripe could not be reached.");
        }
        return await ReadResponseAsync(response, "GET", $"/v1/{path}", loggedHeaders, null, cancellationToken);
    }

    async Task<JsonObject> PostAbsoluteAsync(string url, string logPath, IReadOnlyDictionary<string, object?> body, string idempotencyKey, CancellationToken cancellationToken)
    {
        var requestBody = StripeForm.Encode(body);
        var headers = new Dictionary<string, string>
        {
            ["authorization"] = $"Bearer {_secretKey}",
            ["content-type"] = "application/x-www-form-urlencoded",
            ["idempotency-key"] = idempotencyKey,
            ["stripe-version"] = StripeRequests.ApiVersion,
        };
        var loggedHeaders = BillingRedactor.PickHeaders(headers, BillingRedactor.RequestHeaderNames);
        var loggedBody = BillingRedactor.MaskBody(requestBody, "application/x-www-form-urlencoded");
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secretKey);
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            request.Headers.TryAddWithoutValidation("Stripe-Version", StripeRequests.ApiVersion);
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(requestBody));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            await LogAsync(new HttpEventDraft("outbound", "POST", logPath, loggedHeaders, loggedBody, null, new Dictionary<string, string>(), null, "Stripe could not be reached."), true, cancellationToken);
            throw new BillingException("stripe_request", "Stripe could not be reached.");
        }
        return await ReadResponseAsync(response, "POST", logPath, loggedHeaders, loggedBody, cancellationToken);
    }

    async Task<JsonObject> ReadResponseAsync(HttpResponseMessage response, string method, string logPath, IReadOnlyDictionary<string, string> loggedHeaders, string? loggedBody, CancellationToken cancellationToken)
    {
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = ParseObject(responseText);
        var problem = Problem((int)response.StatusCode, response.IsSuccessStatusCode, parsed);
        var responseHeaders = new Dictionary<string, string>();
        foreach (var header in response.Headers) responseHeaders[header.Key] = string.Join(", ", header.Value);
        foreach (var header in response.Content.Headers) responseHeaders[header.Key] = string.Join(", ", header.Value);
        await LogAsync(new HttpEventDraft("outbound", method, logPath, loggedHeaders, loggedBody, (int)response.StatusCode, BillingRedactor.PickHeaders(responseHeaders, BillingRedactor.ResponseHeaderNames), BillingRedactor.MaskBody(responseText, response.Content.Headers.ContentType?.MediaType ?? "application/json"), problem), problem is not null, cancellationToken);
        if (problem is not null) throw new BillingException("stripe_request", problem);
        return parsed ?? throw new BillingException("stripe_request", "Stripe returned an empty response.");
    }

    async Task LogAsync(HttpEventDraft draft, bool problem, CancellationToken cancellationToken)
    {
        if (!problem && !_debugHttp) return;
        try { await _store.RecordHttpEventAsync(draft, cancellationToken); }
        catch { /* A failed log must not hide the Stripe result. */ }
    }

    static JsonObject? ParseObject(string text)
    {
        if (text.Length == 0) return null;
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    static string? Problem(int status, bool ok, JsonObject? body)
    {
        if (!ok) return StripeMessage(body) ?? $"Stripe rejected the request (HTTP {status}).";
        if (body is null) return "Stripe returned an empty response.";
        return null;
    }

    static string? StripeMessage(JsonObject? body)
    {
        if (body?["error"] is not JsonObject error) return null;
        var message = error["message"]?.GetValue<string>();
        return string.IsNullOrEmpty(message) ? null : message;
    }

    static string? StringField(JsonObject body, string key)
    {
        var value = body[key]?.GetValue<string>();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
