using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Snekl.Domain;

namespace Snekl.Domain.Security;

/// <summary>
/// Binds a member access token to a P-256 key that stays in the browser.
/// The token carries <see cref="OAuthClaims.CnfJkt"/>. Each request carries a short
/// ES256 proof whose public key thumbprint must match.
/// </summary>
public static class DeviceProof
{
    public const string HeaderName = "DPoP";
    public const string DeviceKeyHeaderName = "X-Snekl-Device-Key";
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan FutureSkew = TimeSpan.FromMinutes(1);

    public static bool TryGetThumbprint(JsonElement key, out string thumbprint, out string error)
    {
        thumbprint = "";
        if (key.ValueKind != JsonValueKind.Object)
        {
            error = "A device key from this browser is required.";
            return false;
        }

        return TryGetThumbprint(key.GetRawText(), out thumbprint, out error);
    }

    public static bool TryGetThumbprint(string? json, out string thumbprint, out string error)
    {
        thumbprint = "";
        error = "A device key from this browser is required.";
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("d", out _))
            {
                error = "The device key must be a P-256 public key.";
                return false;
            }

            if (!TryField(root, "kty", out var kty) || !string.Equals(kty, "EC", StringComparison.Ordinal)
                || !TryField(root, "crv", out var crv) || !string.Equals(crv, "P-256", StringComparison.Ordinal)
                || !TryField(root, "x", out var x) || !TryField(root, "y", out var y)
                || !TryNormalizeCoordinate(x, out x) || !TryNormalizeCoordinate(y, out y))
            {
                error = "The device key must be a P-256 public key.";
                return false;
            }

            thumbprint = Thumbprint(x, y);
            error = "";
            return true;
        }
        catch (JsonException)
        {
            error = "The device key must be a P-256 public key.";
            return false;
        }
    }

    public static string Thumbprint(string x, string y)
    {
        var canonical = "{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"" + x + "\",\"y\":\"" + y + "\"}";
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(canonical)));
    }

    public static string? Validate(
        string? proof,
        string expectedThumbprint,
        string method,
        string requestUrl,
        string accessToken,
        DeviceProofReplayCache replay,
        DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(proof) || proof.Length > 8192)
            return "This session is locked to the browser that signed in.";

        var parts = proof.Split('.');
        if (parts.Length != 3)
            return "This session is locked to the browser that signed in.";

        JsonDocument headerDoc;
        JsonDocument payloadDoc;
        try
        {
            headerDoc = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[0])));
            payloadDoc = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[1])));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return "This session is locked to the browser that signed in.";
        }

        using (headerDoc)
        using (payloadDoc)
        {
            var header = headerDoc.RootElement;
            var payload = payloadDoc.RootElement;
            if (!TryField(header, "alg", out var alg) || !string.Equals(alg, "ES256", StringComparison.Ordinal)
                || !TryField(header, "typ", out var typ) || !string.Equals(typ, "dpop+jwt", StringComparison.Ordinal)
                || !header.TryGetProperty("jwk", out var jwk)
                || !TryGetThumbprint(jwk, out var proofThumbprint, out _)
                || !string.Equals(proofThumbprint, expectedThumbprint, StringComparison.Ordinal))
                return "This session is locked to the browser that signed in.";

            if (!TryField(payload, "htm", out var htm)
                || !string.Equals(htm, method, StringComparison.Ordinal)
                || !TryField(payload, "htu", out var htu)
                || !UrlsMatch(htu, requestUrl)
                || !TryField(payload, "jti", out var jti)
                || jti.Length is < 8 or > 128
                || !payload.TryGetProperty("iat", out var iatElement)
                || !iatElement.TryGetInt64(out var iat)
                || !TryField(payload, "ath", out var ath)
                || !string.Equals(ath, AccessTokenHash(accessToken), StringComparison.Ordinal))
                return "This session is locked to the browser that signed in.";

            var issued = DateTimeOffset.FromUnixTimeSeconds(iat);
            if (issued > utcNow.Add(FutureSkew) || utcNow - issued > MaxAge)
                return "This session is locked to the browser that signed in.";

            if (!Verify(parts[0] + "." + parts[1], parts[2], jwk))
                return "This session is locked to the browser that signed in.";

            if (!replay.TryRemember(proofThumbprint + ":" + jti, issued.Add(MaxAge)))
                return "This session is locked to the browser that signed in.";
        }

        return null;
    }

    public static string AccessTokenHash(string accessToken) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));

    public static string Create(
        ECDsa key,
        string x,
        string y,
        string method,
        string url,
        string accessToken,
        DateTimeOffset utcNow,
        string? jti = null)
    {
        var jwk = new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = x,
            ["y"] = y
        };
        var headerJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["typ"] = "dpop+jwt",
            ["alg"] = "ES256",
            ["jwk"] = jwk
        });
        var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["htm"] = method,
            ["htu"] = url,
            ["iat"] = utcNow.ToUnixTimeSeconds(),
            ["jti"] = jti ?? Guid.NewGuid().ToString("N"),
            ["ath"] = AccessTokenHash(accessToken)
        });
        var signingInput = Base64Url(Encoding.UTF8.GetBytes(headerJson)) + "." + Base64Url(Encoding.UTF8.GetBytes(payloadJson));
        var signature = key.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return signingInput + "." + Base64Url(signature);
    }

    public static bool UrlsMatch(string proofUrl, string requestUrl)
    {
        if (!Uri.TryCreate(proofUrl, UriKind.Absolute, out var proof)) return false;
        if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out var request)) return false;
        if (!string.Equals(proof.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(proof.IdnHost, request.IdnHost, StringComparison.OrdinalIgnoreCase)) return false;
        if (Port(proof) != Port(request)) return false;
        if (!string.Equals(proof.AbsolutePath, request.AbsolutePath, StringComparison.Ordinal)) return false;
        return string.Equals(proof.Query, request.Query, StringComparison.Ordinal);
    }

    public static string? ReadThumbprint(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[1])));
            return doc.RootElement.TryGetProperty(OAuthClaims.CnfJkt, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static bool Verify(string signingInput, string signature, JsonElement jwk)
    {
        if (!TryField(jwk, "x", out var x) || !TryField(jwk, "y", out var y)
            || !TryNormalizeCoordinate(x, out x) || !TryNormalizeCoordinate(y, out y))
            return false;

        byte[] sig;
        try
        {
            sig = Base64UrlDecode(signature);
        }
        catch (FormatException)
        {
            return false;
        }

        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = Base64UrlDecode(x), Y = Base64UrlDecode(y) }
        });
        return ecdsa.VerifyData(
            Encoding.ASCII.GetBytes(signingInput),
            sig,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static bool TryField(JsonElement obj, string name, out string value)
    {
        value = "";
        if (!obj.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return false;
        value = text;
        return true;
    }

    private static bool TryNormalizeCoordinate(string raw, out string normalized)
    {
        normalized = "";
        try
        {
            var bytes = Base64UrlDecode(raw);
            if (bytes.Length != 32) return false;
            normalized = Base64Url(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static int Port(Uri uri) =>
        uri.IsDefaultPort ? uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80 : uri.Port;

    public static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            0 => padded,
            _ => throw new FormatException("Invalid base64url length.")
        };
        return Convert.FromBase64String(padded);
    }
}

public sealed class DeviceProofReplayCache
{
    private readonly ConcurrentDictionary<string, long> _seen = new();

    public bool TryRemember(string jti, DateTimeOffset expires)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (_seen.Count > 4096)
        {
            foreach (var entry in _seen)
            {
                if (entry.Value < now)
                    _seen.TryRemove(entry.Key, out _);
            }
        }

        var exp = expires.ToUnixTimeSeconds();
        if (_seen.TryAdd(jti, exp))
            return true;
        if (_seen.TryGetValue(jti, out var existing) && existing < now)
            return _seen.TryUpdate(jti, exp, existing);
        return false;
    }
}
