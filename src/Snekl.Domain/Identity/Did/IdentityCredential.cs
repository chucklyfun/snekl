using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snekl.Domain.Identity.Did;

/// <summary>
/// Minimal W3C Verifiable Credential for cross-auth-server identity linking
/// without mutual partner trust. Issuer is the source auth server's did:web.
/// </summary>
public record IdentityCredential(
    [property: JsonPropertyName("@context")] IReadOnlyList<string> Context,
    IReadOnlyList<string> Type,
    string Issuer,
    IdentityCredentialSubject CredentialSubject,
    string IssuanceDate,
    string ExpirationDate,
    IdentityCredentialProof Proof);

public record IdentityCredentialSubject(
    string Id,
    string CanonicalUsername,
    string DisplayName,
    string Email,
    string Bio,
    string? DisambiguationTag,
    string PrimaryAuthServerUrl);

public record IdentityCredentialProof(
    string Type,
    string Created,
    string VerificationMethod,
    string ProofPurpose,
    string ProofValue);

public static class IdentityCredentialBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static IdentityCredential Create(
        string issuerDid,
        string verificationMethodId,
        byte[] issuerSigningKeyPkcs8,
        IdentityCredentialSubject subject,
        TimeSpan lifetime)
    {
        var now = DateTime.UtcNow;
        var unsigned = new
        {
            Context = new[] { "https://www.w3.org/ns/credentials/v2", "https://snekl.dev/credentials/identity/v1" },
            Type = new[] { "VerifiableCredential", "SneklIdentityCredential" },
            Issuer = issuerDid,
            CredentialSubject = subject,
            IssuanceDate = now.ToString("O"),
            ExpirationDate = now.Add(lifetime).ToString("O")
        };

        var payloadJson = JsonSerializer.Serialize(unsigned, JsonOptions);
        var signature = DidKey.Sign(issuerSigningKeyPkcs8, System.Text.Encoding.UTF8.GetBytes(payloadJson));
        var proofValue = "z" + Base58Encoding.Encode(signature);

        return new IdentityCredential(
            unsigned.Context,
            unsigned.Type,
            issuerDid,
            subject,
            unsigned.IssuanceDate,
            unsigned.ExpirationDate,
            new IdentityCredentialProof(
                "Ed25519Signature2020",
                unsigned.IssuanceDate,
                verificationMethodId,
                "assertionMethod",
                proofValue));
    }

    public static string Serialize(IdentityCredential credential) =>
        JsonSerializer.Serialize(credential, JsonOptions);

    public static IdentityCredential? Deserialize(string json) =>
        JsonSerializer.Deserialize<IdentityCredential>(json, JsonOptions);

    public static bool TryValidate(IdentityCredential credential, string issuerPublicKeyMultibase, out string? error)
    {
        error = null;
        if (DateTime.Parse(credential.ExpirationDate) <= DateTime.UtcNow)
        {
            error = "Credential expired.";
            return false;
        }

        var unsigned = new
        {
            Context = credential.Context,
            Type = credential.Type,
            Issuer = credential.Issuer,
            CredentialSubject = credential.CredentialSubject,
            IssuanceDate = credential.IssuanceDate,
            ExpirationDate = credential.ExpirationDate
        };

        var payloadJson = JsonSerializer.Serialize(unsigned, JsonOptions);
        if (!DidKey.VerifyWithPublicKeyMultibase(issuerPublicKeyMultibase, System.Text.Encoding.UTF8.GetBytes(payloadJson),
                Base58Encoding.Decode(credential.Proof.ProofValue[1..])))
        {
            error = "Invalid credential signature.";
            return false;
        }

        return true;
    }
}
