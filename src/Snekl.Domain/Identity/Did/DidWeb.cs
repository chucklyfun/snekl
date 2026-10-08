namespace Snekl.Domain.Identity.Did;

/// <summary>did:web identifiers for auth servers (hosted DID documents).</summary>
public static class DidWeb
{
    public const string Prefix = "did:web:";

    public static string FromBaseUrl(string baseUrl)
    {
        var uri = new Uri(baseUrl.Trim().TrimEnd('/'));
        var host = uri.Host.ToLowerInvariant();
        if (uri.IsDefaultPort)
            return $"{Prefix}{host}";

        return $"{Prefix}{Uri.EscapeDataString($"{host}:{uri.Port}")}";
    }

    public static string DocumentUrl(string baseUrl) =>
        $"{baseUrl.Trim().TrimEnd('/')}/.well-known/did.json";
}
