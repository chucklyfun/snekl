using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;

namespace Snekl.Domain.Identity.Did;

/// <summary>did:key generation and parsing per W3C did:key method (Ed25519).</summary>
public static class DidKey
{
    public const string Prefix = "did:key:";
    private const byte Ed25519MulticodecPrefix = 0xed;

    public sealed record KeyPair(string Did, byte[] PublicKey, byte[] PrivateKey);

    public static KeyPair Generate()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        var publicKey = ((Ed25519PublicKeyParameters)pair.Public).GetEncoded();
        var privateKey = ((Ed25519PrivateKeyParameters)pair.Private).GetEncoded();
        return new KeyPair(CreateFromPublicKey(publicKey), publicKey, privateKey);
    }

    public static string CreateFromPublicKey(ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.Length != 32)
            throw new ArgumentException("Ed25519 public key must be 32 bytes.", nameof(publicKey));

        var multicodec = new byte[1 + publicKey.Length];
        multicodec[0] = Ed25519MulticodecPrefix;
        publicKey.CopyTo(multicodec.AsSpan(1));
        return Prefix + "z" + Base58Encoding.Encode(multicodec);
    }

    public static bool TryParse(string did, out byte[] publicKey)
    {
        publicKey = [];
        if (!did.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var multibase = did[Prefix.Length..];
        if (multibase.Length < 2 || multibase[0] != 'z') return false;

        try
        {
            var decoded = Base58Encoding.Decode(multibase[1..]);
            if (decoded.Length != 33 || decoded[0] != Ed25519MulticodecPrefix) return false;
            publicKey = decoded[1..];
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string PublicKeyMultibase(string did)
    {
        if (!TryParse(did, out var publicKey))
            throw new ArgumentException("Invalid did:key.", nameof(did));

        var multicodec = new byte[1 + publicKey.Length];
        multicodec[0] = Ed25519MulticodecPrefix;
        Buffer.BlockCopy(publicKey, 0, multicodec, 1, publicKey.Length);
        return "z" + Base58Encoding.Encode(multicodec);
    }

    public static byte[] Sign(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> data)
    {
        var key = new Ed25519PrivateKeyParameters(privateKey.ToArray());
        var signer = new Ed25519Signer();
        signer.Init(true, key);
        signer.BlockUpdate(data);
        return signer.GenerateSignature();
    }

    public static bool Verify(string did, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        if (!TryParse(did, out var publicKey)) return false;
        return VerifyPublicKey(publicKey, data, signature);
    }

    public static bool VerifyWithPublicKeyMultibase(string publicKeyMultibase, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        if (!TryDecodePublicKeyMultibase(publicKeyMultibase, out var publicKey)) return false;
        return VerifyPublicKey(publicKey, data, signature);
    }

    public static bool VerifyMultibaseProof(string did, string payloadJson, string signatureMultibase)
    {
        if (!signatureMultibase.StartsWith('z')) return false;
        var signature = Base58Encoding.Decode(signatureMultibase[1..]);
        return Verify(did, Encoding.UTF8.GetBytes(payloadJson), signature);
    }

    public static bool TryDecodePublicKeyMultibase(string multibase, out byte[] publicKey)
    {
        publicKey = [];
        if (string.IsNullOrWhiteSpace(multibase) || multibase[0] != 'z') return false;

        try
        {
            var decoded = Base58Encoding.Decode(multibase[1..]);
            if (decoded.Length != 33 || decoded[0] != Ed25519MulticodecPrefix) return false;
            publicKey = decoded[1..];
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyPublicKey(byte[] publicKey, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        var key = new Ed25519PublicKeyParameters(publicKey, 0);
        var verifier = new Ed25519Signer();
        verifier.Init(false, key);
        verifier.BlockUpdate(data);
        return verifier.VerifySignature(signature.ToArray());
    }
}
