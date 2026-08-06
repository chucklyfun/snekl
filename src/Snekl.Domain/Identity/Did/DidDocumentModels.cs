namespace Snekl.Domain.Identity.Did;

public record DidDocument(
    IReadOnlyList<object> Context,
    string Id,
    IReadOnlyList<DidVerificationMethod> VerificationMethod,
    IReadOnlyList<string> Authentication,
    IReadOnlyList<string>? AssertionMethod = null);

public record DidVerificationMethod(
    string Id,
    string Type,
    string Controller,
    string PublicKeyMultibase);

public static class DidDocumentBuilder
{
    public static DidDocument ForDidKey(string did)
    {
        var multibase = DidKey.PublicKeyMultibase(did);
        var vmId = $"{did}#{multibase[1..]}";

        return new DidDocument(
            [
                "https://www.w3.org/ns/did/v1",
                "https://w3id.org/security/suites/ed25519-2020/v1"
            ],
            did,
            [new DidVerificationMethod(vmId, "Ed25519VerificationKey2020", did, multibase)],
            [vmId],
            [vmId]);
    }
}
