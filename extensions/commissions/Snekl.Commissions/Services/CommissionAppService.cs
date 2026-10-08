using Microsoft.EntityFrameworkCore;
using Snekl.Commissions.Data;
using Snekl.Commissions.Payments;
using Snekl.Commissions.Workflow;

namespace Snekl.Commissions.Services;

public class CommissionAppService
{
    readonly CommissionsDb _db;
    readonly CommissionsOptions _options;

    public CommissionAppService(CommissionsDb db, Microsoft.Extensions.Options.IOptions<CommissionsOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<StudioDto?> GetStudioAsync(MemberSession member, CancellationToken ct)
    {
        var studio = await FindStudioAsync(member.ContentBaseUrl, member.UserId, ct);
        return studio is null ? null : ToStudioDto(studio);
    }

    public async Task<StudioDto> UpsertStudioAsync(MemberSession member, UpsertStudio request, CancellationToken ct)
    {
        var studio = await FindStudioAsync(member.ContentBaseUrl, member.UserId, ct);
        if (studio is null)
        {
            studio = new ArtistStudio
            {
                ContentBaseUrl = member.ContentBaseUrl,
                UserId = member.UserId,
                Username = member.Username,
                DisplayName = member.DisplayName
            };
            _db.Studios.Add(studio);
        }

        studio.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? member.DisplayName : request.DisplayName.Trim();
        studio.TermsOfService = request.TermsOfService?.Trim() ?? "";
        studio.AcceptingCommissions = request.AcceptingCommissions;
        studio.PortfolioNote = request.PortfolioNote?.Trim() ?? "";
        studio.Username = member.Username;

        if (request.CcBillClientAcc is not null)
            studio.CcBillClientAcc = EmptyToNull(request.CcBillClientAcc);
        if (request.CcBillSubacc is not null)
            studio.CcBillSubacc = EmptyToNull(request.CcBillSubacc);
        if (request.CcBillFlexFormId is not null)
            studio.CcBillFlexFormId = EmptyToNull(request.CcBillFlexFormId);
        if (!string.IsNullOrWhiteSpace(request.CcBillSalt))
            studio.CcBillSalt = request.CcBillSalt.Trim();
        if (request.CcBillAffiliateId is not null)
            studio.CcBillAffiliateId = EmptyToNull(request.CcBillAffiliateId);

        studio.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToStudioDto(studio);
    }

    public async Task<IReadOnlyList<ListingDto>> ListListingsAsync(string contentBaseUrl, Guid? artistUserId, CancellationToken ct)
    {
        var q = _db.Listings.AsNoTracking()
            .Include(l => l.Studio)
            .Where(l => l.Studio!.ContentBaseUrl == contentBaseUrl && l.IsOpen);
        if (artistUserId is Guid id)
            q = q.Where(l => l.Studio!.UserId == id);
        var rows = await q.OrderByDescending(l => l.UpdatedAt).ToListAsync(ct);
        return rows.Select(ToListingDto).ToList();
    }

    public async Task<ListingDto> CreateListingAsync(MemberSession member, CreateListing request, CancellationToken ct)
    {
        var studio = await FindStudioAsync(member.ContentBaseUrl, member.UserId, ct)
            ?? throw new InvalidOperationException("Create a studio before adding listings.");
        var mode = request.PricingMode?.Trim().ToLowerInvariant();
        if (mode is not CommissionModes.Fixed and not CommissionModes.Quote)
            throw new InvalidOperationException("Choose fixed or quote pricing.");
        if (mode == CommissionModes.Fixed && (request.PriceCents is null or <= 0))
            throw new InvalidOperationException("Fixed listings need a price.");

        var listing = new CommissionListing
        {
            StudioId = studio.Id,
            Title = (request.Title ?? "").Trim(),
            Description = (request.Description ?? "").Trim(),
            PricingMode = mode,
            PriceCents = mode == CommissionModes.Fixed ? request.PriceCents : null,
            Currency = "USD",
            OpenSlots = Math.Max(0, request.OpenSlots),
            Terms = request.Terms?.Trim(),
            IsOpen = true,
            Adult = request.Adult,
            UpdatedAt = DateTime.UtcNow
        };
        if (string.IsNullOrWhiteSpace(listing.Title))
            throw new InvalidOperationException("Enter a title.");
        _db.Listings.Add(listing);
        await _db.SaveChangesAsync(ct);
        listing.Studio = studio;
        return ToListingDto(listing);
    }

    public async Task<OrderDto> RequestAsync(MemberSession member, Guid listingId, RequestCommission request, CancellationToken ct)
    {
        var listing = await _db.Listings.Include(l => l.Studio).FirstOrDefaultAsync(l => l.Id == listingId, ct)
            ?? throw new InvalidOperationException("Listing not found.");
        if (!listing.IsOpen || listing.Studio is null)
            throw new InvalidOperationException("That listing is closed.");
        if (listing.Studio.ContentBaseUrl != member.ContentBaseUrl)
            throw new InvalidOperationException("Wrong content server.");
        if (listing.Studio.UserId == member.UserId)
            throw new InvalidOperationException("You cannot request your own listing.");
        if (listing.OpenSlots <= 0 && listing.PricingMode == CommissionModes.Fixed)
            throw new InvalidOperationException("No open slots.");

        var payment = request.PaymentMethod?.Trim().ToLowerInvariant() ?? "";
        var studioReady = CcBillCheckout.StudioReady(
            listing.Studio.CcBillClientAcc,
            listing.Studio.CcBillSubacc,
            listing.Studio.CcBillFlexFormId,
            listing.Studio.CcBillSalt);
        var payError = CommissionRules.PaymentMethodError(listing.Adult, payment, studioReady);
        if (payError is not null)
            throw new InvalidOperationException(payError);

        var order = new CommissionOrder
        {
            ListingId = listing.Id,
            ContentBaseUrl = member.ContentBaseUrl,
            ArtistUserId = listing.Studio.UserId,
            ArtistUsername = listing.Studio.Username,
            ClientUserId = member.UserId,
            ClientUsername = member.Username,
            Brief = (request.Brief ?? "").Trim(),
            Status = CommissionStatuses.Requested,
            PricingMode = listing.PricingMode,
            PriceCents = listing.PriceCents,
            Currency = listing.Currency,
            PaymentMethod = payment,
            EscrowState = CommissionModes.EscrowNone
        };
        _db.Orders.Add(order);
        AddEntry(order, member, "request", $"Requested with {payment}.");
        await _db.SaveChangesAsync(ct);
        return await LoadOrderDtoAsync(order.Id, member, null, ct);
    }

    public async Task<OrderDto> QuoteAsync(MemberSession member, Guid orderId, QuoteRequest request, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        EnsureParty(order, member);
        var error = CommissionRules.QuoteError(order.PricingMode, order.Status, order.ArtistUserId == member.UserId, request.PriceCents);
        if (error is not null)
            throw new InvalidOperationException(error);
        order.PriceCents = request.PriceCents;
        order.Status = CommissionStatuses.Quoted;
        order.UpdatedAt = DateTime.UtcNow;
        AddEntry(order, member, "quote", $"Quoted {request.PriceCents} cents.");
        await _db.SaveChangesAsync(ct);
        return await LoadOrderDtoAsync(order.Id, member, null, ct);
    }

    public async Task<OrderDto> TransitionAsync(
        MemberSession member,
        HostSession? host,
        Guid orderId,
        string status,
        IReadOnlyList<MilestoneDraft>? milestones,
        CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        EnsureParty(order, member);
        var to = status.Trim().ToLowerInvariant();
        var asArtist = order.ArtistUserId == member.UserId;
        var firstPaid = MilestoneRules.FirstPaid(order.Milestones);
        var allPaid = MilestoneRules.AllPaid(order.Milestones);
        var anyBad = MilestoneRules.AnyBad(order.Milestones);
        var error = CommissionRules.TransitionError(
            order.PricingMode,
            order.Status,
            to,
            asArtist,
            order.PaymentMethod,
            order.EscrowState,
            firstPaid,
            allPaid,
            anyBad);
        if (error is not null)
            throw new InvalidOperationException(error);

        if (to == CommissionStatuses.Accepted)
            await EnsureMilestonesOnAcceptAsync(order, milestones, ct);

        var slot = CommissionRules.SlotChange(order.Status, to);
        if (slot != 0)
        {
            var listing = await _db.Listings.FirstAsync(l => l.Id == order.ListingId, ct);
            listing.OpenSlots = Math.Max(0, listing.OpenSlots + slot);
            listing.UpdatedAt = DateTime.UtcNow;
        }

        order.EscrowState = CommissionRules.EscrowAfterTransition(order.PaymentMethod, order.EscrowState, to);
        order.Status = to;
        order.UpdatedAt = DateTime.UtcNow;
        AddEntry(order, member, "status", $"Status → {to}.");
        if (order.EscrowState is CommissionModes.EscrowReleased or CommissionModes.EscrowRefunded)
            AddEntry(order, member, "escrow", $"Escrow marked {order.EscrowState}. This records the agreement and does not move money.");
        await _db.SaveChangesAsync(ct);
        return await LoadOrderDtoAsync(order.Id, member, host, ct);
    }

    public async Task<OrderDto> EscrowAsync(MemberSession member, HostSession? host, Guid orderId, string state, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        EnsureParty(order, member);
        var to = state.Trim().ToLowerInvariant();
        var error = CommissionRules.EscrowError(order.PaymentMethod, order.Status, order.EscrowState, to);
        if (error is not null)
            throw new InvalidOperationException(error);
        order.EscrowState = to;
        order.UpdatedAt = DateTime.UtcNow;
        AddEntry(order, member, "escrow", $"Escrow marked {to}. This records the agreement and does not move money.");
        await _db.SaveChangesAsync(ct);
        return await LoadOrderDtoAsync(order.Id, member, host, ct);
    }

    public async Task<CheckoutDto> StartMilestoneCheckoutAsync(MemberSession member, Guid orderId, Guid milestoneId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (order.ClientUserId != member.UserId)
            throw new InvalidOperationException("Only the client can pay a milestone.");
        var milestone = order.Milestones.FirstOrDefault(m => m.Id == milestoneId)
            ?? throw new InvalidOperationException("Milestone not found.");
        var gate = MilestoneRules.PayGate(order, milestone);
        if (gate is not null)
            throw new InvalidOperationException(gate);

        var studio = await _db.Studios.FirstOrDefaultAsync(
            s => s.ContentBaseUrl == order.ContentBaseUrl && s.UserId == order.ArtistUserId, ct)
            ?? throw new InvalidOperationException("Artist studio not found.");
        if (!CcBillCheckout.StudioReady(studio.CcBillClientAcc, studio.CcBillSubacc, studio.CcBillFlexFormId, studio.CcBillSalt))
            throw new InvalidOperationException("Artist CCBill is not configured.");

        var url = CcBillCheckout.BuildUrl(
            new CcBillCheckoutRequest(
                studio.CcBillFlexFormId!,
                studio.CcBillClientAcc!,
                studio.CcBillSubacc!,
                milestone.AmountCents,
                order.Id,
                milestone.Id,
                studio.CcBillAffiliateId),
            studio.CcBillSalt!);
        AddEntry(order, member, "payment", $"Opened CCBill checkout for milestone {milestone.Sequence}: {milestone.Label}.");
        await _db.SaveChangesAsync(ct);
        return new CheckoutDto(url, milestone.Id, milestone.AmountCents);
    }

    public async Task<(int StatusCode, string Body)> HandleCcBillWebhookAsync(IReadOnlyDictionary<string, string> form, CancellationToken ct)
    {
        var payload = CcBillWebhook.Parse(form);
        if (string.IsNullOrWhiteSpace(payload.TransactionId))
            return (400, "missing transaction");

        var seen = await _db.CcBillWebhookEvents.AnyAsync(
            e => e.TransactionId == payload.TransactionId && e.EventType == payload.EventType && e.Outcome == "applied", ct);
        if (seen)
            return (200, "duplicate");

        if (payload.OrderId is null || payload.MilestoneId is null)
        {
            _db.CcBillWebhookEvents.Add(new CcBillWebhookEvent
            {
                TransactionId = payload.TransactionId!,
                EventType = payload.EventType,
                Outcome = "rejected"
            });
            await _db.SaveChangesAsync(ct);
            return (400, "missing ids");
        }

        var order = await _db.Orders
            .Include(o => o.Milestones)
            .Include(o => o.Listing)!.ThenInclude(l => l!.Studio)
            .FirstOrDefaultAsync(o => o.Id == payload.OrderId, ct);
        if (order?.Listing?.Studio is null)
            return (404, "order not found");

        var studio = order.Listing.Studio;
        var milestone = order.Milestones.FirstOrDefault(m => m.Id == payload.MilestoneId);
        if (milestone is null)
            return (404, "milestone not found");

        var (outcome, message) = CcBillWebhook.Apply(payload, studio, order, milestone, alreadySeenTransaction: false);
        _db.CcBillWebhookEvents.Add(new CcBillWebhookEvent
        {
            TransactionId = payload.TransactionId!,
            EventType = payload.EventType,
            Outcome = outcome.ToString().ToLowerInvariant(),
            MilestoneId = milestone.Id
        });

        if (outcome == CcBillWebhookOutcome.Applied)
        {
            order.UpdatedAt = DateTime.UtcNow;
            _db.Timeline.Add(new CommissionTimelineEntry
            {
                OrderId = order.Id,
                ActorUserId = Guid.Empty,
                ActorUsername = "ccbill",
                Kind = "payment",
                Body = $"{message} txn={payload.TransactionId}"
            });
            await _db.SaveChangesAsync(ct);
            return (200, "ok");
        }

        await _db.SaveChangesAsync(ct);
        return outcome switch
        {
            CcBillWebhookOutcome.Duplicate => (200, "duplicate"),
            CcBillWebhookOutcome.Ignored => (200, "ignored"),
            _ => (400, message)
        };
    }

    public async Task<IReadOnlyList<OrderDto>> ListOrdersAsync(MemberSession member, CancellationToken ct)
    {
        var ids = await _db.Orders.AsNoTracking()
            .Where(o => o.ContentBaseUrl == member.ContentBaseUrl
                && (o.ArtistUserId == member.UserId || o.ClientUserId == member.UserId))
            .OrderByDescending(o => o.UpdatedAt)
            .Select(o => o.Id)
            .ToListAsync(ct);
        var list = new List<OrderDto>();
        foreach (var id in ids)
            list.Add(await LoadOrderDtoAsync(id, member, null, ct));
        return list;
    }

    public async Task<OrderDto> GetOrderAsync(MemberSession member, Guid orderId, CancellationToken ct) =>
        await LoadOrderDtoAsync(orderId, member, null, ct);

    public async Task<OrderDto> AddNoteAsync(MemberSession member, Guid orderId, string body, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        EnsureParty(order, member);
        if (!CommissionRules.CanAddNote(order.Status))
            throw new InvalidOperationException("This commission is closed.");
        AddEntry(order, member, "note", body.Trim());
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await LoadOrderDtoAsync(order.Id, member, null, ct);
    }

    async Task EnsureMilestonesOnAcceptAsync(CommissionOrder order, IReadOnlyList<MilestoneDraft>? milestones, CancellationToken ct)
    {
        if (order.PaymentMethod != CommissionModes.CcBill)
            return;
        if (order.PriceCents is null or <= 0)
            throw new InvalidOperationException("Set a price before accepting.");

        var draft = (milestones is { Count: > 0 }
            ? milestones.Select(m => (m.Label?.Trim() ?? "", m.AmountCents)).ToList()
            : MilestoneRules.DefaultMilestones(order.PriceCents.Value).ToList());

        var error = MilestoneRules.ValidateDraft(order.PriceCents.Value, draft);
        if (error is not null)
            throw new InvalidOperationException(error);

        if (order.Milestones.Count > 0)
            _db.Milestones.RemoveRange(order.Milestones.ToList());

        var seq = 1;
        foreach (var (label, amount) in draft)
        {
            var milestone = new CommissionMilestone
            {
                OrderId = order.Id,
                Sequence = seq++,
                Label = label,
                AmountCents = amount,
                Status = MilestoneStatuses.Pending
            };
            // Client-generated Guids must be Added explicitly or EF treats them as updates.
            _db.Milestones.Add(milestone);
            order.Milestones.Add(milestone);
        }
        await Task.CompletedTask;
    }

    async Task<CommissionOrder> LoadOrderAsync(Guid orderId, CancellationToken ct)
    {
        // Drop any stale tracked graph so Includes load milestones/timeline from the database.
        _db.ChangeTracker.Clear();
        return await _db.Orders
                   .Include(o => o.Milestones)
                   .Include(o => o.Timeline)
                   .Include(o => o.Listing)
                   .FirstOrDefaultAsync(o => o.Id == orderId, ct)
               ?? throw new InvalidOperationException("Order not found.");
    }

    async Task<OrderDto> LoadOrderDtoAsync(Guid orderId, MemberSession member, HostSession? host, CancellationToken ct)
    {
        var order = await _db.Orders.AsNoTracking()
            .Include(o => o.Milestones)
            .Include(o => o.Timeline)
            .Include(o => o.Listing)
            .FirstAsync(o => o.Id == orderId, ct);
        if (order.ContentBaseUrl != member.ContentBaseUrl
            || (order.ArtistUserId != member.UserId && order.ClientUserId != member.UserId && host is null))
            throw new InvalidOperationException("Order not found.");
        return ToOrderDto(order, member);
    }

    async Task<ArtistStudio?> FindStudioAsync(string contentBaseUrl, Guid userId, CancellationToken ct) =>
        await _db.Studios.FirstOrDefaultAsync(s => s.ContentBaseUrl == contentBaseUrl && s.UserId == userId, ct);

    static void EnsureParty(CommissionOrder order, MemberSession member)
    {
        if (order.ContentBaseUrl != member.ContentBaseUrl
            || (order.ArtistUserId != member.UserId && order.ClientUserId != member.UserId))
            throw new InvalidOperationException("Order not found.");
    }

    void AddEntry(CommissionOrder order, MemberSession member, string kind, string body)
    {
        var entry = new CommissionTimelineEntry
        {
            OrderId = order.Id,
            ActorUserId = member.UserId,
            ActorUsername = member.Username,
            Kind = kind,
            Body = body
        };
        _db.Timeline.Add(entry);
        order.Timeline.Add(entry);
    }

    StudioDto ToStudioDto(ArtistStudio studio) => new(
        studio.Id,
        studio.UserId,
        studio.Username,
        studio.DisplayName,
        studio.TermsOfService,
        studio.AcceptingCommissions,
        studio.PortfolioNote,
        studio.CcBillClientAcc,
        studio.CcBillSubacc,
        studio.CcBillFlexFormId,
        !string.IsNullOrWhiteSpace(studio.CcBillSalt),
        studio.CcBillAffiliateId,
        CcBillCheckout.StudioReady(studio.CcBillClientAcc, studio.CcBillSubacc, studio.CcBillFlexFormId, studio.CcBillSalt),
        _options.WebhookUrl);

    static ListingDto ToListingDto(CommissionListing listing) => new(
        listing.Id,
        listing.StudioId,
        listing.Studio?.UserId ?? Guid.Empty,
        listing.Studio?.Username ?? "",
        listing.Title,
        listing.Description,
        listing.PricingMode,
        listing.PriceCents,
        listing.Currency,
        listing.OpenSlots,
        listing.Terms,
        listing.IsOpen,
        listing.Adult);

    OrderDto ToOrderDto(CommissionOrder order, MemberSession member)
    {
        var asArtist = order.ArtistUserId == member.UserId;
        var actions = new List<string>();
        foreach (var to in new[]
                 {
                     CommissionStatuses.Accepted, CommissionStatuses.Quoted, CommissionStatuses.InProgress,
                     CommissionStatuses.Delivered, CommissionStatuses.Completed, CommissionStatuses.Declined,
                     CommissionStatuses.Cancelled
                 })
        {
            if (CommissionRules.TransitionError(
                    order.PricingMode, order.Status, to, asArtist, order.PaymentMethod, order.EscrowState,
                    MilestoneRules.FirstPaid(order.Milestones),
                    MilestoneRules.AllPaid(order.Milestones),
                    MilestoneRules.AnyBad(order.Milestones)) is null)
                actions.Add(to);
        }
        if (CommissionRules.EscrowError(order.PaymentMethod, order.Status, order.EscrowState, CommissionModes.EscrowHeld) is null)
            actions.Add("hold");
        if (CommissionRules.EscrowError(order.PaymentMethod, order.Status, order.EscrowState, CommissionModes.EscrowReleased) is null)
            actions.Add("release");
        var next = MilestoneRules.NextPayable(order.Milestones);
        if (!asArtist && next is not null && MilestoneRules.PayGate(order, next) is null)
            actions.Add("pay_milestone");

        return new OrderDto(
            order.Id,
            order.ListingId,
            order.Listing?.Title ?? "",
            order.ArtistUsername,
            order.ClientUsername,
            order.Brief,
            order.Status,
            order.PricingMode,
            order.PriceCents,
            order.Currency,
            order.PaymentMethod,
            order.EscrowState,
            order.CreatedAt,
            order.UpdatedAt,
            order.Timeline.OrderBy(t => t.CreatedAt).Select(t => new TimelineDto(t.Id, t.ActorUsername, t.Kind, t.Body, t.CreatedAt)).ToList(),
            actions,
            order.Milestones.OrderBy(m => m.Sequence).Select(m => new MilestoneDto(m.Id, m.Sequence, m.Label, m.AmountCents, m.Status, m.PaidAt)).ToList(),
            next?.Id);
    }

    static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record UpsertStudio(
    string? DisplayName,
    string? TermsOfService,
    bool AcceptingCommissions,
    string? PortfolioNote,
    string? CcBillClientAcc,
    string? CcBillSubacc,
    string? CcBillFlexFormId,
    string? CcBillSalt,
    string? CcBillAffiliateId);

public record CreateListing(
    string? Title,
    string? Description,
    string? PricingMode,
    int? PriceCents,
    int OpenSlots,
    string? Terms,
    bool Adult);

public record RequestCommission(string? Brief, string? PaymentMethod);
public record QuoteRequest(int PriceCents);
public record MilestoneDraft(string? Label, int AmountCents);
public record TransitionRequest(string? Status, IReadOnlyList<MilestoneDraft>? Milestones);

public record StudioDto(
    Guid Id,
    Guid UserId,
    string Username,
    string DisplayName,
    string TermsOfService,
    bool AcceptingCommissions,
    string PortfolioNote,
    string? CcBillClientAcc,
    string? CcBillSubacc,
    string? CcBillFlexFormId,
    bool CcBillSaltSet,
    string? CcBillAffiliateId,
    bool CcBillReady,
    string CcBillWebhookUrl);

public record ListingDto(
    Guid Id,
    Guid StudioId,
    Guid ArtistUserId,
    string ArtistUsername,
    string Title,
    string Description,
    string PricingMode,
    int? PriceCents,
    string Currency,
    int OpenSlots,
    string? Terms,
    bool IsOpen,
    bool Adult);

public record TimelineDto(Guid Id, string ActorUsername, string Kind, string Body, DateTime CreatedAt);
public record MilestoneDto(Guid Id, int Sequence, string Label, int AmountCents, string Status, DateTime? PaidAt);
public record CheckoutDto(string Url, Guid MilestoneId, int AmountCents);

public record OrderDto(
    Guid Id,
    Guid ListingId,
    string ListingTitle,
    string ArtistUsername,
    string ClientUsername,
    string Brief,
    string Status,
    string PricingMode,
    int? PriceCents,
    string Currency,
    string PaymentMethod,
    string EscrowState,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<TimelineDto> Timeline,
    IReadOnlyList<string> Actions,
    IReadOnlyList<MilestoneDto> Milestones,
    Guid? NextMilestoneId);
