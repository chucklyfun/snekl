namespace Snekl.Domain.Identity.Did;

/// <summary>Base58-BTC encoding (Bitcoin alphabet) used by multibase in did:key.</summary>
public static class Base58Encoding
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return string.Empty;

        var zeros = 0;
        while (zeros < data.Length && data[zeros] == 0) zeros++;

        var buffer = new byte[data.Length * 2];
        var length = 0;

        for (var i = zeros; i < data.Length; i++)
        {
            var carry = data[i];
            for (var j = 0; j < length; j++)
            {
                carry += (byte)(buffer[j] * 256);
                buffer[j] = (byte)(carry % 58);
                carry /= 58;
            }

            while (carry > 0)
            {
                buffer[length++] = (byte)(carry % 58);
                carry /= 58;
            }
        }

        var result = new char[zeros + length];
        for (var i = 0; i < zeros; i++) result[i] = '1';
        for (var i = 0; i < length; i++) result[zeros + i] = Alphabet[buffer[length - 1 - i]];

        return new string(result);
    }

    public static byte[] Decode(string encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return [];

        var zeros = 0;
        while (zeros < encoded.Length && encoded[zeros] == '1') zeros++;

        var buffer = new byte[encoded.Length];
        var length = 0;

        for (var i = zeros; i < encoded.Length; i++)
        {
            var digit = Alphabet.IndexOf(encoded[i]);
            if (digit < 0) throw new FormatException($"Invalid base58 character: {encoded[i]}");

            var carry = digit;
            for (var j = 0; j < length; j++)
            {
                carry += buffer[j] * 58;
                buffer[j] = (byte)(carry % 256);
                carry /= 256;
            }

            while (carry > 0)
            {
                buffer[length++] = (byte)(carry % 256);
                carry /= 256;
            }
        }

        var result = new byte[zeros + length];
        for (var i = 0; i < length; i++) result[zeros + length - 1 - i] = buffer[i];
        return result;
    }
}
