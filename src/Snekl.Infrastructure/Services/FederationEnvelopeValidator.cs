using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Snekl.Domain.Dtos;
using Snekl.Domain.Identity.Did;
using Snekl.Domain.Security;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IFederationEnvelopeValidator
{
    Task ValidateAsync(FederationEnvelope envelope, CancellationToken ct = default);
}

public class FederationEnvelopeValidator : IFederationEnvelopeValidator
{
    private readonly SneklDbContext _db;
    private readonly INodeDidResolver _didResolver;
    private readonly bool _requireSignatures;

    public FederationEnvelopeValidator(
        SneklDbContext db,
        INodeDidResolver didResolver,
        IConfiguration config)
    {
        _db = db;
        _didResolver = didResolver;
        _requireSignatures = config.GetValue("Federation:RequireSignatures", true);
    }

    public async Task ValidateAsync(FederationEnvelope envelope, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(envelope.Type))
            throw new InvalidOperationException("Federation envelope type is required.");

        if (!FederationEnvelopeSigner.IsTimestampValid(envelope.Timestamp, DateTime.UtcNow))
            throw new InvalidOperationException("Federation envelope timestamp is outside the allowed window.");

        var normalizedUrl = envelope.SourceNodeUrl.Trim().TrimEnd('/');

        if (string.IsNullOrWhiteSpace(envelope.Signature))
        {
            if (_requireSignatures)
                throw new InvalidOperationException("Federation envelope signature is required.");
            return;
        }

        var verificationKey = await ResolveVerificationKeyAsync(envelope, normalizedUrl, ct);
        if (string.IsNullOrWhiteSpace(verificationKey))
            throw new InvalidOperationException("Unable to resolve federation source verification key.");

        var valid = FederationEnvelopeSigner.Verify(
            verificationKey,
            envelope.Type,
            envelope.SourceNodeId,
            normalizedUrl,
            envelope.Timestamp,
            envelope.Payload,
            envelope.Signature);

        if (!valid)
            throw new InvalidOperationException("Invalid federation envelope signature.");

        if (envelope.Type == "node.subscribe")
            return;

        var peer = await _db.PeerNodes.FirstOrDefaultAsync(
            p => p.IsActive &&
                 (p.BaseUrl == normalizedUrl || p.RemoteNodeId == envelope.SourceNodeId),
            ct);

        if (peer is null)
            throw new InvalidOperationException("Federation envelope source is not a subscribed peer.");
    }

    private async Task<string?> ResolveVerificationKeyAsync(
        FederationEnvelope envelope,
        string normalizedUrl,
        CancellationToken ct)
    {
        var peer = await _db.PeerNodes.FirstOrDefaultAsync(
            p => p.BaseUrl == normalizedUrl || p.RemoteNodeId == envelope.SourceNodeId,
            ct);

        if (!string.IsNullOrWhiteSpace(peer?.VerificationKeyMultibase))
            return peer.VerificationKeyMultibase;

        var did = string.IsNullOrWhiteSpace(peer?.RemoteDid)
            ? DidWeb.FromBaseUrl(normalizedUrl)
            : peer.RemoteDid;
        var verificationKey = await _didResolver.ResolveVerificationKeyMultibaseAsync(did, normalizedUrl, ct);

        if (peer is not null && !string.IsNullOrWhiteSpace(verificationKey))
        {
            peer.VerificationKeyMultibase = verificationKey;
            peer.RemoteDid = did;
            await _db.SaveChangesAsync(ct);
        }

        return verificationKey;
    }
}
