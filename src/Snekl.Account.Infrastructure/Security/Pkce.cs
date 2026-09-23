using System.Security.Cryptography;
using System.Text;

namespace Snekl.Account.Infrastructure.Security;

public static class Pkce
{
    public static bool IsValidVerifier(string verifier) =>
        verifier.Length is >= 43 and <= 128;

    public static string S256Challenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64Url(hash);
    }

    public static bool VerifyS256(string verifier, string challenge) =>
        string.Equals(S256Challenge(verifier), challenge, StringComparison.Ordinal);

    public static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
