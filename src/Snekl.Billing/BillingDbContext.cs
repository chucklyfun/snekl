using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Snekl.Billing;

public sealed class BillingSubscriptionRow
{
    public string Id { get; set; } = "";
    public string? BusinessAccountId { get; set; }
    public string? ContactEmail { get; set; }
    public string ServicesJson { get; set; } = "[]";
    public string Status { get; set; } = "";
    public int? MonthlyAmountCents { get; set; }
    public string Currency { get; set; } = "usd";
    public string? StripeSessionId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string? StripeCustomerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class BillingCommissionRow
{
    public string OrderId { get; set; } = "";
    public string ArtistAccountId { get; set; } = "";
    public string Status { get; set; } = "";
    public int AmountCents { get; set; }
    public int PlatformFeeCents { get; set; }
    public int ArtistCents { get; set; }
    public string Currency { get; set; } = "usd";
    public string? StripePaymentIntentId { get; set; }
    public string? StripeChargeId { get; set; }
    public string? StripeTransferId { get; set; }
    public string? StripeRefundId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class BillingLedgerRow
{
    public string Id { get; set; } = "";
    public string? StripeEventId { get; set; }
    public string Kind { get; set; } = "";
    public string SubjectId { get; set; } = "";
    public int? AmountCents { get; set; }
    public string DetailJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public sealed class BillingStripeEventRow
{
    public string StripeEventId { get; set; } = "";
    public string EventType { get; set; } = "";
    public string Outcome { get; set; } = "";
    public DateTime ReceivedAt { get; set; }
}

public sealed class BillingModificationRow
{
    public long Seq { get; set; }
    public string Id { get; set; } = "";
    public string PaymentKind { get; set; } = "";
    public string PaymentId { get; set; } = "";
    public string Cause { get; set; } = "";
    public string Action { get; set; } = "";
    public string? StripeEventId { get; set; }
    public string? BeforeJson { get; set; }
    public string AfterJson { get; set; } = "{}";
    public string ChangesJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
}

public sealed class BillingConnectedAccountRow
{
    public string ArtistId { get; set; } = "";
    public string StripeAccountId { get; set; } = "";
    public string? Email { get; set; }
    public bool DetailsSubmitted { get; set; }
    public bool ChargesEnabled { get; set; }
    public bool PayoutsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class BillingHttpEventRow
{
    public long Seq { get; set; }
    public string Id { get; set; } = "";
    public string Direction { get; set; } = "";
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string RequestHeadersJson { get; set; } = "{}";
    public string? RequestBody { get; set; }
    public int? ResponseStatus { get; set; }
    public string ResponseHeadersJson { get; set; } = "{}";
    public string? ResponseBody { get; set; }
    public string? Problem { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class BillingDbContext : DbContext
{
    public BillingDbContext(DbContextOptions<BillingDbContext> options) : base(options) { }

    public DbSet<BillingSubscriptionRow> Subscriptions => Set<BillingSubscriptionRow>();
    public DbSet<BillingCommissionRow> Commissions => Set<BillingCommissionRow>();
    public DbSet<BillingLedgerRow> Ledger => Set<BillingLedgerRow>();
    public DbSet<BillingStripeEventRow> StripeEvents => Set<BillingStripeEventRow>();
    public DbSet<BillingModificationRow> Modifications => Set<BillingModificationRow>();
    public DbSet<BillingHttpEventRow> HttpEvents => Set<BillingHttpEventRow>();
    public DbSet<BillingConnectedAccountRow> ConnectedAccounts => Set<BillingConnectedAccountRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var json = Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true ? "jsonb" : "text";
        Subscription(modelBuilder.Entity<BillingSubscriptionRow>(), json);
        Commission(modelBuilder.Entity<BillingCommissionRow>());
        ConfigureLedger(modelBuilder.Entity<BillingLedgerRow>(), json);
        StripeEvent(modelBuilder.Entity<BillingStripeEventRow>());
        Modification(modelBuilder.Entity<BillingModificationRow>(), json);
        Http(modelBuilder.Entity<BillingHttpEventRow>(), json);
        Connected(modelBuilder.Entity<BillingConnectedAccountRow>());
    }

    static void Connected(EntityTypeBuilder<BillingConnectedAccountRow> entity)
    {
        entity.ToTable("billing_connected_accounts");
        entity.HasKey(row => row.ArtistId);
        entity.Property(row => row.ArtistId).HasColumnName("artist_id");
        entity.Property(row => row.StripeAccountId).HasColumnName("stripe_account_id");
        entity.Property(row => row.Email).HasColumnName("email");
        entity.Property(row => row.DetailsSubmitted).HasColumnName("details_submitted");
        entity.Property(row => row.ChargesEnabled).HasColumnName("charges_enabled");
        entity.Property(row => row.PayoutsEnabled).HasColumnName("payouts_enabled");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(row => row.StripeAccountId).IsUnique();
    }

    static void Subscription(EntityTypeBuilder<BillingSubscriptionRow> entity, string json)
    {
        entity.ToTable("billing_subscriptions");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id");
        entity.Property(row => row.BusinessAccountId).HasColumnName("business_account_id");
        entity.Property(row => row.ContactEmail).HasColumnName("contact_email");
        entity.Property(row => row.ServicesJson).HasColumnName("services").HasColumnType(json);
        entity.Property(row => row.Status).HasColumnName("status");
        entity.Property(row => row.MonthlyAmountCents).HasColumnName("monthly_amount_cents");
        entity.Property(row => row.Currency).HasColumnName("currency");
        entity.Property(row => row.StripeSessionId).HasColumnName("stripe_session_id");
        entity.Property(row => row.StripeSubscriptionId).HasColumnName("stripe_subscription_id");
        entity.Property(row => row.StripeCustomerId).HasColumnName("stripe_customer_id");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(row => row.StripeSessionId).IsUnique();
        entity.HasIndex(row => row.StripeSubscriptionId).IsUnique();
        entity.HasIndex(row => new { row.BusinessAccountId, row.CreatedAt });
    }

    static void Commission(EntityTypeBuilder<BillingCommissionRow> entity)
    {
        entity.ToTable("billing_commissions");
        entity.HasKey(row => row.OrderId);
        entity.Property(row => row.OrderId).HasColumnName("order_id");
        entity.Property(row => row.ArtistAccountId).HasColumnName("artist_account_id");
        entity.Property(row => row.Status).HasColumnName("status");
        entity.Property(row => row.AmountCents).HasColumnName("amount_cents");
        entity.Property(row => row.PlatformFeeCents).HasColumnName("platform_fee_cents");
        entity.Property(row => row.ArtistCents).HasColumnName("artist_cents");
        entity.Property(row => row.Currency).HasColumnName("currency");
        entity.Property(row => row.StripePaymentIntentId).HasColumnName("stripe_payment_intent_id");
        entity.Property(row => row.StripeChargeId).HasColumnName("stripe_charge_id");
        entity.Property(row => row.StripeTransferId).HasColumnName("stripe_transfer_id");
        entity.Property(row => row.StripeRefundId).HasColumnName("stripe_refund_id");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(row => row.StripePaymentIntentId).IsUnique();
    }

    static void ConfigureLedger(EntityTypeBuilder<BillingLedgerRow> entity, string json)
    {
        entity.ToTable("billing_ledger");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id");
        entity.Property(row => row.StripeEventId).HasColumnName("stripe_event_id");
        entity.Property(row => row.Kind).HasColumnName("kind");
        entity.Property(row => row.SubjectId).HasColumnName("subject_id");
        entity.Property(row => row.AmountCents).HasColumnName("amount_cents");
        entity.Property(row => row.DetailJson).HasColumnName("detail").HasColumnType(json);
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.HasIndex(row => row.StripeEventId).IsUnique();
        entity.HasIndex(row => new { row.SubjectId, row.CreatedAt });
    }

    static void StripeEvent(EntityTypeBuilder<BillingStripeEventRow> entity)
    {
        entity.ToTable("billing_stripe_events");
        entity.HasKey(row => row.StripeEventId);
        entity.Property(row => row.StripeEventId).HasColumnName("stripe_event_id");
        entity.Property(row => row.EventType).HasColumnName("event_type");
        entity.Property(row => row.Outcome).HasColumnName("outcome");
        entity.Property(row => row.ReceivedAt).HasColumnName("received_at");
    }

    static void Modification(EntityTypeBuilder<BillingModificationRow> entity, string json)
    {
        entity.ToTable("billing_payment_modifications");
        entity.HasKey(row => row.Seq);
        entity.Property(row => row.Seq).HasColumnName("seq").ValueGeneratedNever();
        entity.Property(row => row.Id).HasColumnName("id");
        entity.Property(row => row.PaymentKind).HasColumnName("payment_kind");
        entity.Property(row => row.PaymentId).HasColumnName("payment_id");
        entity.Property(row => row.Cause).HasColumnName("cause");
        entity.Property(row => row.Action).HasColumnName("action");
        entity.Property(row => row.StripeEventId).HasColumnName("stripe_event_id");
        entity.Property(row => row.BeforeJson).HasColumnName("before_snapshot").HasColumnType(json);
        entity.Property(row => row.AfterJson).HasColumnName("after_snapshot").HasColumnType(json);
        entity.Property(row => row.ChangesJson).HasColumnName("changes").HasColumnType(json);
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.HasIndex(row => row.Id).IsUnique();
        entity.HasIndex(row => new { row.PaymentKind, row.PaymentId, row.Seq });
    }

    static void Http(EntityTypeBuilder<BillingHttpEventRow> entity, string json)
    {
        entity.ToTable("billing_http_events");
        entity.HasKey(row => row.Seq);
        entity.Property(row => row.Seq).HasColumnName("seq").ValueGeneratedNever();
        entity.Property(row => row.Id).HasColumnName("id");
        entity.Property(row => row.Direction).HasColumnName("direction");
        entity.Property(row => row.Method).HasColumnName("method");
        entity.Property(row => row.Path).HasColumnName("path");
        entity.Property(row => row.RequestHeadersJson).HasColumnName("request_headers").HasColumnType(json);
        entity.Property(row => row.RequestBody).HasColumnName("request_body");
        entity.Property(row => row.ResponseStatus).HasColumnName("response_status");
        entity.Property(row => row.ResponseHeadersJson).HasColumnName("response_headers").HasColumnType(json);
        entity.Property(row => row.ResponseBody).HasColumnName("response_body");
        entity.Property(row => row.Problem).HasColumnName("problem");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.HasIndex(row => row.Id).IsUnique();
        entity.HasIndex(row => row.CreatedAt);
    }
}

public static class BillingRows
{
    public static BillingState ToState(
        IEnumerable<BillingSubscriptionRow> subscriptions,
        IEnumerable<BillingCommissionRow> commissions,
        IEnumerable<BillingLedgerRow> ledger,
        IEnumerable<BillingStripeEventRow> events,
        IEnumerable<BillingModificationRow> modifications,
        IEnumerable<BillingHttpEventRow> httpEvents,
        IEnumerable<BillingConnectedAccountRow> connectedAccounts)
    {
        var state = new BillingState();
        foreach (var row in subscriptions) state.Subscriptions.Add(ToSubscription(row));
        foreach (var row in commissions) state.Commissions.Add(ToCommission(row));
        foreach (var row in ledger) state.Ledger.Add(new LedgerEntry(row.Id, row.StripeEventId, row.Kind, row.SubjectId, row.AmountCents, row.DetailJson, row.CreatedAt));
        foreach (var row in events) state.StripeEvents.Add((row.StripeEventId, row.EventType, row.Outcome, row.ReceivedAt));
        foreach (var row in modifications.OrderBy(row => row.Seq)) state.Modifications.Add(ToModification(row));
        foreach (var row in httpEvents.OrderBy(row => row.Seq)) state.HttpEvents.Add(ToHttp(row));
        foreach (var row in connectedAccounts) state.ConnectedAccounts.Add(ToConnected(row));
        return state;
    }

    public static BillingConnectedAccountRow ToRow(ConnectedAccountRecord record) => new()
    {
        ArtistId = record.ArtistId,
        StripeAccountId = record.StripeAccountId,
        Email = record.Email,
        DetailsSubmitted = record.DetailsSubmitted,
        ChargesEnabled = record.ChargesEnabled,
        PayoutsEnabled = record.PayoutsEnabled,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt,
    };

    static ConnectedAccountRecord ToConnected(BillingConnectedAccountRow row) =>
        new(row.ArtistId, row.StripeAccountId, row.Email, row.DetailsSubmitted, row.ChargesEnabled, row.PayoutsEnabled, row.CreatedAt, row.UpdatedAt);

    public static BillingSubscriptionRow ToRow(SubscriptionRecord record) => new()
    {
        Id = record.Id,
        BusinessAccountId = record.BusinessAccountId,
        ContactEmail = record.ContactEmail,
        ServicesJson = JsonSerializer.Serialize(record.Services, BillingJson.Options),
        Status = record.Status,
        MonthlyAmountCents = record.MonthlyAmountCents,
        Currency = record.Currency,
        StripeSessionId = record.StripeSessionId,
        StripeSubscriptionId = record.StripeSubscriptionId,
        StripeCustomerId = record.StripeCustomerId,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt,
    };

    public static BillingCommissionRow ToRow(CommissionRecord record) => new()
    {
        OrderId = record.OrderId,
        ArtistAccountId = record.ArtistAccountId,
        Status = record.Status,
        AmountCents = record.AmountCents,
        PlatformFeeCents = record.PlatformFeeCents,
        ArtistCents = record.ArtistCents,
        Currency = record.Currency,
        StripePaymentIntentId = record.StripePaymentIntentId,
        StripeChargeId = record.StripeChargeId,
        StripeTransferId = record.StripeTransferId,
        StripeRefundId = record.StripeRefundId,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt,
    };

    static SubscriptionRecord ToSubscription(BillingSubscriptionRow row) => new(
        row.Id, row.BusinessAccountId, row.ContactEmail,
        JsonSerializer.Deserialize<List<string>>(row.ServicesJson, BillingJson.Options) ?? [],
        row.Status, row.MonthlyAmountCents, row.Currency, row.StripeSessionId, row.StripeSubscriptionId, row.StripeCustomerId, row.CreatedAt, row.UpdatedAt);

    static CommissionRecord ToCommission(BillingCommissionRow row) => new(
        row.OrderId, row.ArtistAccountId, row.Status, row.AmountCents, row.PlatformFeeCents, row.ArtistCents, row.Currency,
        row.StripePaymentIntentId, row.StripeChargeId, row.StripeTransferId, row.StripeRefundId, row.CreatedAt, row.UpdatedAt);

    static PaymentModification ToModification(BillingModificationRow row) => new(
        row.Id, row.PaymentKind, row.PaymentId, row.Cause, row.Action, row.StripeEventId,
        ParseElement(row.BeforeJson), ParseElement(row.AfterJson) ?? default,
        JsonSerializer.Deserialize<List<FieldChange>>(row.ChangesJson, BillingJson.Options) ?? [],
        row.CreatedAt);

    static HttpEvent ToHttp(BillingHttpEventRow row) => new(
        row.Id, row.Direction, row.Method, row.Path,
        JsonSerializer.Deserialize<Dictionary<string, string>>(row.RequestHeadersJson, BillingJson.Options) ?? [],
        row.RequestBody, row.ResponseStatus,
        JsonSerializer.Deserialize<Dictionary<string, string>>(row.ResponseHeadersJson, BillingJson.Options) ?? [],
        row.ResponseBody, row.Problem, row.CreatedAt);

    public static string? WriteElement(JsonElement? element) => element?.GetRawText();

    static JsonElement? ParseElement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
