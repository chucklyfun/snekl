namespace Snekl.Commissions.Workflow;

public static class CommissionModes
{
    public const string Fixed = "fixed";
    public const string Quote = "quote";
    public const string OffPlatform = "off_platform";
    public const string Escrow = "escrow";
    public const string CcBill = "ccbill";
    public const string EscrowNone = "none";
    public const string EscrowHeld = "held";
    public const string EscrowReleased = "released";
    public const string EscrowRefunded = "refunded";
}

public static class CommissionStatuses
{
    public const string Requested = "requested";
    public const string Quoted = "quoted";
    public const string Accepted = "accepted";
    public const string InProgress = "in_progress";
    public const string Delivered = "delivered";
    public const string Completed = "completed";
    public const string Declined = "declined";
    public const string Cancelled = "cancelled";

    public static bool HoldsSlot(string status) =>
        status is Accepted or InProgress or Delivered or Completed;

    public static bool IsTerminal(string status) =>
        status is Completed or Declined or Cancelled;
}

public static class CommissionRules
{
    public static string? QuoteError(string pricingMode, string status, bool actorIsArtist, int priceCents)
    {
        if (!actorIsArtist)
            return "Only the artist can send a quote.";
        if (pricingMode != CommissionModes.Quote)
            return "This commission has a fixed price.";
        if (status is not CommissionStatuses.Requested and not CommissionStatuses.Quoted)
            return "A quote can only be sent while the request is open.";
        if (priceCents <= 0)
            return "Enter a price.";
        return null;
    }

    public static string? PaymentMethodError(bool adult, string paymentMethod, bool studioHasCcBill)
    {
        if (paymentMethod is not CommissionModes.OffPlatform
            and not CommissionModes.Escrow
            and not CommissionModes.CcBill)
            return "Choose off-platform, escrow, or CCBill.";

        if (adult && paymentMethod == CommissionModes.Escrow)
            return "Adult commissions cannot use escrow (Stripe). Use CCBill or off-platform.";

        if (!adult && paymentMethod == CommissionModes.CcBill)
            return "CCBill is only for adult listings.";

        if (paymentMethod == CommissionModes.CcBill && !studioHasCcBill)
            return "The artist has not connected a CCBill account yet.";

        return null;
    }

    public static string? TransitionError(
        string pricingMode,
        string from,
        string to,
        bool actorIsArtist,
        string paymentMethod,
        string escrowState,
        bool firstMilestonePaid = true,
        bool allMilestonesPaid = true,
        bool anyMilestoneBad = false)
    {
        if (from == to)
            return "That commission is already in this state.";
        if (CommissionStatuses.IsTerminal(from))
            return "This commission is already closed.";

        var allowed = Allowed(pricingMode, from, to, actorIsArtist);
        if (!allowed)
            return actorIsArtist
                ? "You cannot make that change as the artist."
                : "You cannot make that change as the client.";

        if (to == CommissionStatuses.Completed
            && paymentMethod == CommissionModes.Escrow
            && escrowState == CommissionModes.EscrowNone)
            return "Mark the escrow as held before completing.";

        if (to == CommissionStatuses.InProgress
            && paymentMethod == CommissionModes.CcBill
            && !firstMilestonePaid)
            return "The client must pay the first CCBill milestone before work starts.";

        if (to == CommissionStatuses.Completed
            && paymentMethod == CommissionModes.CcBill
            && anyMilestoneBad)
            return "A milestone was refunded or charged back; this order cannot be completed.";

        if (to == CommissionStatuses.Completed
            && paymentMethod == CommissionModes.CcBill
            && !allMilestonesPaid)
            return "Every CCBill milestone must be paid before completing.";

        return null;
    }

    public static int SlotChange(string from, string to)
    {
        var before = CommissionStatuses.HoldsSlot(from);
        var after = CommissionStatuses.HoldsSlot(to);
        if (!before && after)
            return -1;
        if (before && !after)
            return 1;
        return 0;
    }

    public static string EscrowAfterTransition(string paymentMethod, string escrowState, string to)
    {
        if (paymentMethod != CommissionModes.Escrow)
            return CommissionModes.EscrowNone;
        if (to == CommissionStatuses.Completed && escrowState == CommissionModes.EscrowHeld)
            return CommissionModes.EscrowReleased;
        if ((to == CommissionStatuses.Cancelled || to == CommissionStatuses.Declined)
            && escrowState == CommissionModes.EscrowHeld)
            return CommissionModes.EscrowRefunded;
        return escrowState;
    }

    public static string? EscrowError(string paymentMethod, string status, string fromEscrow, string toEscrow)
    {
        if (paymentMethod != CommissionModes.Escrow)
            return "This commission is paid off-platform.";
        if (fromEscrow == toEscrow)
            return "Escrow is already in that state.";
        if (fromEscrow is CommissionModes.EscrowReleased or CommissionModes.EscrowRefunded)
            return "That escrow record is already closed.";

        if (fromEscrow == CommissionModes.EscrowNone
            && toEscrow == CommissionModes.EscrowHeld
            && status is CommissionStatuses.Accepted or CommissionStatuses.InProgress or CommissionStatuses.Delivered)
            return null;

        if (fromEscrow == CommissionModes.EscrowHeld
            && toEscrow == CommissionModes.EscrowReleased
            && status is CommissionStatuses.Delivered or CommissionStatuses.Completed)
            return null;

        if (fromEscrow == CommissionModes.EscrowHeld
            && toEscrow == CommissionModes.EscrowRefunded
            && status is CommissionStatuses.Cancelled or CommissionStatuses.Declined)
            return null;

        return "That escrow change is not available.";
    }

    public static bool CanAddNote(string status) => !CommissionStatuses.IsTerminal(status);

    private static bool Allowed(string pricingMode, string from, string to, bool actorIsArtist) =>
        (pricingMode, from, to, actorIsArtist) switch
        {
            (CommissionModes.Quote, CommissionStatuses.Requested, CommissionStatuses.Declined, true) => true,
            (_, CommissionStatuses.Requested, CommissionStatuses.Cancelled, false) => true,
            (CommissionModes.Fixed, CommissionStatuses.Requested, CommissionStatuses.Accepted, true) => true,
            (CommissionModes.Fixed, CommissionStatuses.Requested, CommissionStatuses.Declined, true) => true,
            (CommissionModes.Quote, CommissionStatuses.Quoted, CommissionStatuses.Declined, true) => true,
            (CommissionModes.Quote, CommissionStatuses.Quoted, CommissionStatuses.Accepted, false) => true,
            (CommissionModes.Quote, CommissionStatuses.Quoted, CommissionStatuses.Cancelled, false) => true,
            (_, CommissionStatuses.Accepted, CommissionStatuses.InProgress, true) => true,
            (_, CommissionStatuses.Accepted, CommissionStatuses.Cancelled, true) => true,
            (_, CommissionStatuses.Accepted, CommissionStatuses.Cancelled, false) => true,
            (_, CommissionStatuses.InProgress, CommissionStatuses.Delivered, true) => true,
            (_, CommissionStatuses.Delivered, CommissionStatuses.Completed, false) => true,
            _ => false
        };
}
