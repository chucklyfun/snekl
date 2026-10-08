namespace Snekl.Account.Domain.Entities;

/// <summary>
/// A business customer that signed up from the public business page. The contact person gets a
/// normal Snekl Account (<see cref="PersonCluster"/>); this row holds the business details and the
/// services they asked about so a host operator can follow up (for example, spawn a hosted server).
/// </summary>
public class BusinessAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PersonClusterId { get; set; }
    public PersonCluster? PersonCluster { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string? Website { get; set; }
    /// <summary>JSON array of service keys from <c>BusinessServiceKeys</c>.</summary>
    public string ServicesJson { get; set; } = "[]";
    /// <summary>Free text for the "other" service option.</summary>
    public string? OtherServices { get; set; }
    public string? Message { get; set; }
    /// <summary>One of <c>BusinessAccountStatuses</c>. New signups start as pending.</summary>
    public string Status { get; set; } = "pending";
    /// <summary>When an auth operator approved or rejected the signup.</summary>
    public DateTime? ReviewedAt { get; set; }
    /// <summary>Operator username (or DID) that approved or rejected the signup.</summary>
    public string? ReviewedBy { get; set; }
    /// <summary>
    /// Content server (<c>NodeConfig.Id</c>) the business created after approval. Null until the
    /// owner creates their hosted server. One server per business account.
    /// </summary>
    public Guid? ServerId { get; set; }
    /// <summary>DID of the linked content server (for links such as <c>/s/{did}/owner</c>).</summary>
    public string? ServerDid { get; set; }
    /// <summary>Base URL of the content host that runs the linked server.</summary>
    public string? ServerBaseUrl { get; set; }
    /// <summary>When a trusted content host reported the server over the signed channel.</summary>
    public DateTime? ServerLinkedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
