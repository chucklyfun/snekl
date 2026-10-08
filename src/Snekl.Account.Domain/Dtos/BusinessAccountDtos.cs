namespace Snekl.Account.Domain.Dtos;

public static class BusinessServiceKeys
{
    public const string ServerHosting = "server-hosting";
    public const string ManagedAuth = "managed-auth";
    public const string Extensions = "extensions";
    public const string Migration = "migration";
    public const string Support = "support";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All =
        [ServerHosting, ManagedAuth, Extensions, Migration, Support, Other];
}

public static class BusinessAccountStatuses
{
    public const string Pending = "pending";
    /// <summary>An operator approved the signup. The owner may create one hosted server.</summary>
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

/// <summary>
/// Public business signup. Creates a Snekl Account for the contact person (same rules as
/// <see cref="RegisterRequest"/>, with a server-generated did:key) plus a pending business account.
/// </summary>
public record BusinessSignupRequest(
    string BusinessName,
    string ContactName,
    string Email,
    string Username,
    string Password,
    IReadOnlyList<string>? Services,
    string? OtherServices = null,
    string? Website = null,
    string? Message = null,
    DevicePublicJwk? DeviceKey = null);

public record BusinessAccountDto(
    Guid Id,
    string BusinessName,
    string ContactName,
    string ContactEmail,
    string? Website,
    IReadOnlyList<string> Services,
    string? OtherServices,
    string? Message,
    string Status,
    Guid PersonClusterId,
    string Username,
    bool EmailVerified,
    DateTime CreatedAt,
    DateTime? ReviewedAt = null,
    string? ReviewedBy = null,
    Guid? ServerId = null,
    string? ServerDid = null,
    string? ServerBaseUrl = null,
    DateTime? ServerLinkedAt = null);

public enum BusinessReviewOutcome
{
    /// <summary>The account moved from pending to the requested status.</summary>
    Changed,
    /// <summary>The account already had the requested status; nothing changed.</summary>
    Unchanged,
    NotFound,
    /// <summary>The account is in a different final status (for example, approving a rejected account).</summary>
    Conflict
}

public sealed record BusinessReviewResult(BusinessReviewOutcome Outcome, BusinessAccountDto? Account, string? Error = null);

public enum BusinessServerLinkOutcome
{
    /// <summary>The server was linked by this call.</summary>
    Linked,
    /// <summary>The same server was already linked; nothing changed (safe retry).</summary>
    AlreadyLinked,
    /// <summary>No business account with that id belongs to the member.</summary>
    NotFound,
    /// <summary>The business account is pending or rejected.</summary>
    NotApproved,
    /// <summary>The business account already has a different server.</summary>
    LinkedToOtherServer,
    /// <summary>The server is already linked to another business account (unique index).</summary>
    ServerInUse
}
