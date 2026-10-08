using System.Security.Cryptography;
using System.Text;

namespace Snekl.Account.Infrastructure.Security;

public static class SecretProtector
{
    public static byte[] DeriveKey(string keyMaterial)
    {
        if (string.IsNullOrWhiteSpace(keyMaterial))
            throw new InvalidOperationException("Two-factor protection key is not configured.");

        try
        {
            var raw = Convert.FromBase64String(keyMaterial);
            if (raw.Length == 32)
                return raw;
        }
        catch (FormatException)
        {
            // Fall through and hash the configured material.
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
    }

    public static string Protect(string plaintext, string keyMaterial)
    {
        var key = DeriveKey(keyMaterial);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plain, cipher, tag);

        var payload = new byte[nonce.Length + cipher.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(cipher, 0, payload, nonce.Length, cipher.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length + cipher.Length, tag.Length);
        return Convert.ToBase64String(payload);
    }

    public static string Unprotect(string payload, string keyMaterial)
    {
        var key = DeriveKey(keyMaterial);
        var bytes = Convert.FromBase64String(payload);
        if (bytes.Length < 12 + 16)
            throw new CryptographicException("Two-factor secret payload is truncated.");

        var nonce = bytes.AsSpan(0, 12);
        var tag = bytes.AsSpan(bytes.Length - 16, 16);
        var cipher = bytes.AsSpan(12, bytes.Length - 12 - 16);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
