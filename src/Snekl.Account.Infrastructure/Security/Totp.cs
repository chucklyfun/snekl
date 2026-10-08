using System.Security.Cryptography;
using System.Text;

namespace Snekl.Account.Infrastructure.Security;

/// <summary>RFC 6238 TOTP (SHA-1, 30-second step, 6 digits) compatible with authenticator apps.</summary>
public static class Totp
{
    public const int Digits = 6;
    public const int PeriodSeconds = 30;
    private const int Window = 1;

    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        return Base32.Encode(bytes);
    }

    public static string BuildOtpAuthUri(string issuer, string account, string base32Secret)
    {
        var safeIssuer = issuer.Replace(":", " ", StringComparison.Ordinal).Trim();
        if (string.IsNullOrEmpty(safeIssuer))
            safeIssuer = "Snekl";
        var safeAccount = account.Replace(":", " ", StringComparison.Ordinal).Trim();
        var label = Uri.EscapeDataString($"{safeIssuer}:{safeAccount}");
        var query = string.Join('&',
            $"secret={Uri.EscapeDataString(base32Secret)}",
            $"issuer={Uri.EscapeDataString(safeIssuer)}",
            "algorithm=SHA1",
            $"digits={Digits}",
            $"period={PeriodSeconds}");
        return $"otpauth://totp/{label}?{query}";
    }

    public static string ComputeCode(string base32Secret, DateTimeOffset at, int digits = Digits, int periodSeconds = PeriodSeconds) =>
        ComputeCode(Base32.Decode(base32Secret), at, digits, periodSeconds);

    public static string ComputeCode(byte[] key, DateTimeOffset at, int digits = Digits, int periodSeconds = PeriodSeconds)
    {
        var step = at.ToUnixTimeSeconds() / periodSeconds;
        return ComputeAtStep(key, step, digits);
    }

    public static bool TryMatch(
        string base32Secret,
        string code,
        DateTimeOffset time,
        long? lastAcceptedStep,
        out long matchedStep)
    {
        matchedStep = 0;
        byte[] key;
        try
        {
            key = Base32.Decode(base32Secret);
        }
        catch (FormatException)
        {
            return false;
        }

        var normalized = new string(code.Where(char.IsDigit).ToArray());
        if (normalized.Length != Digits)
            return false;

        var current = time.ToUnixTimeSeconds() / PeriodSeconds;
        long? found = null;
        for (var delta = -Window; delta <= Window; delta++)
        {
            var step = current + delta;
            if (step < 0)
                continue;

            var expected = ComputeAtStep(key, step, Digits);
            var equal = CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(normalized));
            var fresh = lastAcceptedStep is null || step > lastAcceptedStep.Value;
            if (equal && fresh && found is null)
                found = step;
        }

        if (found is null)
            return false;

        matchedStep = found.Value;
        return true;
    }

    private static string ComputeAtStep(byte[] key, long step, int digits)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);
        var mod = 1;
        for (var i = 0; i < digits; i++)
            mod *= 10;
        return (binary % mod).ToString().PadLeft(digits, '0');
    }
}

public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return string.Empty;

        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        var bits = 0;
        var value = 0;
        foreach (var b in data)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Alphabet[(value >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
            output.Append(Alphabet[(value << (5 - bits)) & 31]);

        return output.ToString();
    }

    public static byte[] Decode(string input)
    {
        var cleaned = input.Trim().TrimEnd('=').Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        var output = new List<byte>(cleaned.Length * 5 / 8);
        var bits = 0;
        var value = 0;
        foreach (var c in cleaned)
        {
            var index = Alphabet.IndexOf(c);
            if (index < 0)
                throw new FormatException("Invalid base32.");
            value = (value << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return output.ToArray();
    }
}
