using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Snekl.Domain.Identity.Did;

namespace Snekl.Domain.Security;

public static class FederationEnvelopeSigner
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static readonly TimeSpan MaxEnvelopeAge = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(1);

    public static string Sign(
        byte[] privateKey,
        string type,
        Guid sourceNodeId,
        string sourceNodeUrl,
        DateTime timestamp,
        object payload)
    {
        var canonical = BuildCanonical(type, sourceNodeId, sourceNodeUrl, timestamp, payload);
        var signature = DidKey.Sign(privateKey, Encoding.UTF8.GetBytes(canonical));
        return "z" + Base58Encoding.Encode(signature);
    }

    public static bool Verify(
        string verificationKeyMultibase,
        string type,
        Guid sourceNodeId,
        string sourceNodeUrl,
        DateTime timestamp,
        object payload,
        string signature)
    {
        if (!TryDecodeSignature(signature, out var signatureBytes)) return false;

        var canonical = BuildCanonical(type, sourceNodeId, sourceNodeUrl, timestamp, payload);
        return DidKey.VerifyWithPublicKeyMultibase(
            verificationKeyMultibase,
            Encoding.UTF8.GetBytes(canonical),
            signatureBytes);
    }

    public static bool IsTimestampValid(DateTime timestamp, DateTime utcNow)
    {
        if (timestamp.Kind == DateTimeKind.Unspecified)
            timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
        else if (timestamp.Kind == DateTimeKind.Local)
            timestamp = timestamp.ToUniversalTime();

        if (timestamp > utcNow.Add(MaxClockSkew)) return false;
        return timestamp >= utcNow.Subtract(MaxEnvelopeAge);
    }

    private static string BuildCanonical(
        string type,
        Guid sourceNodeId,
        string sourceNodeUrl,
        DateTime timestamp,
        object payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload, PayloadJsonOptions);
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson))).ToLowerInvariant();
        var normalizedUrl = sourceNodeUrl.Trim().TrimEnd('/');
        var ts = timestamp.Kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Local => timestamp.ToUniversalTime(),
            _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
        };

        return $"{type}|{sourceNodeId:D}|{normalizedUrl}|{ts:O}|{payloadHash}";
    }

    private static bool TryDecodeSignature(string signature, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(signature) || signature[0] != 'z') return false;
        try
        {
            bytes = Base58Encoding.Decode(signature[1..]);
            return bytes.Length == 64;
        }
        catch
        {
            return false;
        }
    }
}
