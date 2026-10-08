using Snekl.Domain.Dtos;

namespace Snekl.Domain.Security;

/// <summary>
/// Builds signed <see cref="FederationEnvelope"/>s for the business hosting channel. This is a thin
/// wrapper over <see cref="FederationEnvelopeSigner"/>: no new crypto, same canonical form, same
/// timestamp window.
/// </summary>
public static class BusinessHostingEnvelope
{
    public static string NewNonce() => Guid.NewGuid().ToString("N");

    public static string NormalizeUrl(string? url) => (url ?? string.Empty).Trim().TrimEnd('/');

    public static FederationEnvelope Seal(
        byte[] privateKey,
        string type,
        Guid sourceNodeId,
        string sourceNodeUrl,
        IBusinessHostingPayload payload,
        DateTime? timestamp = null)
    {
        var ts = timestamp ?? DateTime.UtcNow;
        var url = NormalizeUrl(sourceNodeUrl);
        var signature = FederationEnvelopeSigner.Sign(privateKey, type, sourceNodeId, url, ts, payload);
        return new FederationEnvelope(type, sourceNodeId, url, ts, payload, signature);
    }
}
