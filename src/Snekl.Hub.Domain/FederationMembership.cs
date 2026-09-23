namespace Snekl.Hub.Domain;

public static class FederationMembership
{
    public const string StatusPending = "pending";
    public const string StatusActive = "active";

    public const string InitiatorHub = "hub";
    public const string InitiatorOrigin = "origin";

    public static string NormalizeInitiator(string? value)
    {
        if (string.Equals(value, InitiatorHub, StringComparison.OrdinalIgnoreCase))
            return InitiatorHub;
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, InitiatorOrigin, StringComparison.OrdinalIgnoreCase))
            return InitiatorOrigin;

        throw new InvalidOperationException("InitiatedBy must be 'hub' or 'origin'.");
    }
}
