namespace Snekl.Domain.Dtos;

/// <summary>
/// Envelope types for the signed Content -> Auth business hosting channel. Requests are
/// <see cref="FederationEnvelope"/>s signed with the content host's server key (the same
/// <c>FederationEnvelopeSigner</c> scheme used for federation and hub ingest).
/// </summary>
public static class BusinessHostingEnvelopeTypes
{
    /// <summary>Ask Auth for a member's business approval status and linked server.</summary>
    public const string Status = "business.hosting.status";

    /// <summary>Tell Auth which server the content host created for a business.</summary>
    public const string Link = "business.hosting.link";
}

/// <summary>Business status values as Auth reports them (same strings as <c>BusinessAccountStatuses</c>).</summary>
public static class BusinessHostingStatusNames
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

/// <summary>
/// Fields every signed business hosting payload carries. <see cref="SignerDid"/> names the trusted
/// content server, <see cref="Audience"/> is the Auth base URL the request is meant for, and
/// <see cref="Nonce"/> is single-use so a captured request cannot be replayed.
/// </summary>
public interface IBusinessHostingPayload
{
    string SignerDid { get; }
    string Audience { get; }
    string Nonce { get; }
}

public sealed record BusinessHostingStatusQuery(
    string SignerDid,
    string Audience,
    string Nonce,
    string MemberDid,
    Guid? BusinessAccountId = null) : IBusinessHostingPayload;

public sealed record BusinessHostingLinkCommand(
    string SignerDid,
    string Audience,
    string Nonce,
    string MemberDid,
    Guid BusinessAccountId,
    Guid ServerId,
    string ServerDid,
    string ServerBaseUrl) : IBusinessHostingPayload;

/// <summary>What Auth returns about a member's business account.</summary>
public sealed record BusinessHostingStatus(
    Guid BusinessAccountId,
    string BusinessName,
    string Status,
    string MemberDid,
    string Username,
    Guid? ServerId,
    string? ServerDid,
    string? ServerBaseUrl);

/// <summary>Link outcome. <see cref="Changed"/> is false when the same server was already linked.</summary>
public sealed record BusinessHostingLinkResult(BusinessHostingStatus Business, bool Changed);
