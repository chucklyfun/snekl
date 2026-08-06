using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Snekl.Account.Domain.Dtos;

namespace Snekl.Account.Infrastructure.Security;

public static class VouchSigner
{
    public static string Sign(object payload, string signingKey)
    {
        var json = JsonSerializer.Serialize(payload);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(hash);
    }

    public static bool Verify(object payload, string signature, string signingKey)
    {
        var expected = Sign(payload, signingKey);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }

    public static string BuildDisplayLabel(string username, string? tag, string primaryAuthServerUrl)
    {
        if (string.IsNullOrWhiteSpace(tag))
            tag = DeriveDefaultTag(primaryAuthServerUrl);
        return string.IsNullOrWhiteSpace(tag) ? username : $"{username} · {tag}";
    }

    public static string DeriveDefaultTag(string primaryAuthServerUrl)
    {
        if (string.IsNullOrWhiteSpace(primaryAuthServerUrl)) return string.Empty;
        try
        {
            var uri = new Uri(primaryAuthServerUrl);
            var host = uri.Host.Split('.')[0];
            return host.Replace("auth-", "").Replace("localhost", "local");
        }
        catch
        {
            return string.Empty;
        }
    }
}

public record UnsignedVouch(
    Guid PersonClusterId,
    string PersonDid,
    Guid SourceIdentityId,
    string SourceAuthServerUrl,
    string CanonicalUsername,
    string DisplayName,
    string Email,
    string Bio,
    string? DisambiguationTag,
    DateTime ExpiresAt);

public static class VouchBuilder
{
    public static VouchPayload Create(UnsignedVouch data, string signingKey)
    {
        var unsigned = new
        {
            data.PersonClusterId,
            data.PersonDid,
            data.SourceIdentityId,
            data.SourceAuthServerUrl,
            data.CanonicalUsername,
            data.DisplayName,
            data.Email,
            data.Bio,
            data.DisambiguationTag,
            data.ExpiresAt
        };
        var signature = VouchSigner.Sign(unsigned, signingKey);
        return new VouchPayload(
            data.PersonClusterId,
            data.PersonDid,
            data.SourceIdentityId,
            data.SourceAuthServerUrl,
            data.CanonicalUsername,
            data.DisplayName,
            data.Email,
            data.Bio,
            data.DisambiguationTag,
            data.ExpiresAt,
            signature);
    }

    public static bool TryValidate(VouchPayload vouch, string partnerSigningKey)
    {
        if (vouch.ExpiresAt <= DateTime.UtcNow) return false;
        var unsigned = new
        {
            vouch.PersonClusterId,
            vouch.PersonDid,
            SourceIdentityId = vouch.SourceIdentityId,
            vouch.SourceAuthServerUrl,
            vouch.CanonicalUsername,
            vouch.DisplayName,
            vouch.Email,
            vouch.Bio,
            vouch.DisambiguationTag,
            vouch.ExpiresAt
        };
        return VouchSigner.Verify(unsigned, vouch.Signature, partnerSigningKey);
    }
}
