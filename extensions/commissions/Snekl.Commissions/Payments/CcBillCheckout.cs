using System.Globalization;
using System.Text;

namespace Snekl.Commissions.Payments;

public sealed record CcBillCheckoutRequest(
    string FlexFormId,
    string ClientAcc,
    string Subacc,
    int AmountCents,
    Guid OrderId,
    Guid MilestoneId,
    string? AffiliateId);

public static class CcBillCheckout
{
    public const string FlexFormsBase = "https://api.ccbill.com/wap-frontflex/flexforms/";

    public static string BuildUrl(CcBillCheckoutRequest request, string salt)
    {
        if (string.IsNullOrWhiteSpace(request.FlexFormId))
            throw new InvalidOperationException("CCBill FlexForms id is required.");
        if (string.IsNullOrWhiteSpace(request.ClientAcc))
            throw new InvalidOperationException("CCBill client account is required.");
        if (string.IsNullOrWhiteSpace(request.Subacc))
            throw new InvalidOperationException("CCBill subaccount is required.");

        var digest = CcBillDigest.FormDigest(request.AmountCents, salt);
        var q = new StringBuilder();
        void Add(string key, string value)
        {
            if (q.Length > 0) q.Append('&');
            q.Append(Uri.EscapeDataString(key));
            q.Append('=');
            q.Append(Uri.EscapeDataString(value));
        }

        Add("clientAccnum", request.ClientAcc.Trim());
        Add("clientSubacc", request.Subacc.Trim());
        Add("initialPrice", CcBillDigest.FormatPrice(request.AmountCents));
        Add("initialPeriod", CcBillDigest.NonRecurringPeriodDays.ToString(CultureInfo.InvariantCulture));
        Add("currencyCode", CcBillDigest.UsdCurrencyCode.ToString(CultureInfo.InvariantCulture));
        Add("formDigest", digest);
        Add("snekl_order_id", request.OrderId.ToString("D"));
        Add("snekl_milestone_id", request.MilestoneId.ToString("D"));
        if (!string.IsNullOrWhiteSpace(request.AffiliateId))
            Add("ccbill_referer", request.AffiliateId.Trim());

        return FlexFormsBase + request.FlexFormId.Trim() + "?" + q;
    }

    public static bool StudioReady(string? clientAcc, string? subacc, string? flexFormId, string? salt) =>
        !string.IsNullOrWhiteSpace(clientAcc)
        && !string.IsNullOrWhiteSpace(subacc)
        && !string.IsNullOrWhiteSpace(flexFormId)
        && !string.IsNullOrWhiteSpace(salt);
}
