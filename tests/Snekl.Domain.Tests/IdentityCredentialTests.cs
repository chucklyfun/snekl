using System.Globalization;
using System.Text.Json;
using Snekl.Domain.Identity.Did;

namespace Snekl.Domain.Tests;

public class IdentityCredentialTests
{
    [Fact]
    public void CreateSerializeDeserialize_PreservesSubjectAndProof()
    {
        var keys = DidKey.Generate();
        var issuerDid = DidWeb.FromBaseUrl("http://localhost:5100");
        var subject = new IdentityCredentialSubject(
            keys.Did,
            "alice",
            "Alice",
            "alice@example.com",
            "bio",
            null,
            "http://localhost:5100");

        var credential = IdentityCredentialBuilder.Create(
            issuerDid,
            $"{issuerDid}#key-1",
            keys.PrivateKey,
            subject,
            TimeSpan.FromHours(1));

        Assert.Equal(issuerDid, credential.Issuer);
        Assert.Equal("alice", credential.CredentialSubject.CanonicalUsername);
        Assert.StartsWith("z", credential.Proof.ProofValue);
        Assert.True(
            DateTime.Parse(credential.ExpirationDate, null, DateTimeStyles.RoundtripKind)
            > DateTime.UtcNow);

        var json = IdentityCredentialBuilder.Serialize(credential);
        var parsed = IdentityCredentialBuilder.Deserialize(json);
        Assert.NotNull(parsed);
        Assert.Equal(credential.Issuer, parsed!.Issuer);
        Assert.Equal(credential.Proof.ProofValue, parsed.Proof.ProofValue);
        Assert.Equal(credential.CredentialSubject.Email, parsed.CredentialSubject.Email);
    }

    [Fact]
    public void Create_SignatureMatchesCanonicalUnsignedPayload()
    {
        var keys = DidKey.Generate();
        var issuerDid = DidWeb.FromBaseUrl("https://auth.example.com");
        var subject = new IdentityCredentialSubject(
            keys.Did,
            "bob",
            "Bob",
            "bob@example.com",
            "",
            null,
            "https://auth.example.com");

        var credential = IdentityCredentialBuilder.Create(
            issuerDid,
            $"{issuerDid}#key-1",
            keys.PrivateKey,
            subject,
            TimeSpan.FromHours(1));

        var unsigned = new
        {
            Context = credential.Context,
            Type = credential.Type,
            Issuer = credential.Issuer,
            CredentialSubject = credential.CredentialSubject,
            IssuanceDate = credential.IssuanceDate,
            ExpirationDate = credential.ExpirationDate
        };
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        var payloadJson = JsonSerializer.Serialize(unsigned, options);
        var signature = Base58Encoding.Decode(credential.Proof.ProofValue[1..]);

        Assert.True(DidKey.Verify(keys.Did, System.Text.Encoding.UTF8.GetBytes(payloadJson), signature));

        var other = DidKey.Generate();
        Assert.False(DidKey.Verify(other.Did, System.Text.Encoding.UTF8.GetBytes(payloadJson), signature));
    }
}
