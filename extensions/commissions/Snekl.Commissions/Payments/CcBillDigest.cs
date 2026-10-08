using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Snekl.Commissions.Payments;

/// <summary>
/// CCBill FlexForms dynamic-pricing digests.
/// See https://ccbill.com/doc/formdigest-value
/// </summary>
public static class CcBillDigest
{
    public const int UsdCurrencyCode = 840;
    public const int NonRecurringPeriodDays = 2;

    public static string FormatPrice(int amountCents) =>
        (amountCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    public static int ParsePriceToCents(string? price)
    {
        if (string.IsNullOrWhiteSpace(price))
            throw new InvalidOperationException("Missing charged price.");
        if (!decimal.TryParse(price.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var dollars))
            throw new InvalidOperationException("Charged price is not a number.");
        return (int)Math.Round(dollars * 100m, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// formDigest for a non-recurring FlexForms price:
    /// MD5(initialPrice + initialPeriod + currencyCode + salt)
    /// </summary>
    public static string FormDigest(int amountCents, string salt, int initialPeriodDays = NonRecurringPeriodDays, int currencyCode = UsdCurrencyCode)
    {
        if (string.IsNullOrWhiteSpace(salt))
            throw new InvalidOperationException("CCBill salt is required.");
        var material =
            FormatPrice(amountCents)
            + initialPeriodDays.ToString(CultureInfo.InvariantCulture)
            + currencyCode.ToString(CultureInfo.InvariantCulture)
            + salt.Trim();
        return Md5Hex(material);
    }

    public static bool FormDigestMatches(int amountCents, string salt, string? provided, int initialPeriodDays = NonRecurringPeriodDays, int currencyCode = UsdCurrencyCode)
    {
        if (string.IsNullOrWhiteSpace(provided))
            return false;
        var expected = FormDigest(amountCents, salt, initialPeriodDays, currencyCode);
        var left = Encoding.ASCII.GetBytes(expected);
        var right = Encoding.ASCII.GetBytes(provided.Trim().ToLowerInvariant());
        if (left.Length != right.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    /// <summary>
    /// Response digest check used on Dynamic Pricing webhooks when CCBill returns
    /// dynamicPricingValidationDigest / responseDigest. Material matches formDigest inputs
    /// for the charged amount when the merchant salt is available.
    /// </summary>
    public static bool ResponseDigestMatches(int amountCents, string salt, string? provided, int initialPeriodDays = NonRecurringPeriodDays, int currencyCode = UsdCurrencyCode) =>
        FormDigestMatches(amountCents, salt, provided, initialPeriodDays, currencyCode);

    static string Md5Hex(string material)
    {
        var hash = MD5.HashData(Encoding.ASCII.GetBytes(material));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
