namespace Snekl.Billing;

public sealed class BillingException : Exception
{
    public BillingException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}
