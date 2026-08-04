using System.Text;
using Snekl.Domain.Identity.Did;

namespace Snekl.Tests.Shared;

public static class DidProofHelper
{
    public static (DidKey.KeyPair Keys, string Proof) CreateRegistrationProof(string username)
    {
        var keys = DidKey.Generate();
        var challenge = $"snekl-register:{username.Trim().ToLowerInvariant()}:{keys.Did}";
        var signature = DidKey.Sign(keys.PrivateKey, Encoding.UTF8.GetBytes(challenge));
        var proof = "z" + Base58Encoding.Encode(signature);
        return (keys, proof);
    }
}
