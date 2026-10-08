using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;
using BusinessHostingLinkCommand = Snekl.Domain.Dtos.BusinessHostingLinkCommand;
using BusinessHostingStatus = Snekl.Domain.Dtos.BusinessHostingStatus;

namespace Snekl.Account.Infrastructure.Services;

public interface IBusinessAccountService
{
    /// <summary>Creates the contact's Snekl Account and a pending business account in one transaction.</summary>
    Task<BusinessAccountDto> SignUpAsync(BusinessSignupRequest request, string deviceKeyThumbprint, CancellationToken ct = default);

    /// <summary>Every business account on this auth server, newest first. Operator use only.</summary>
    Task<IReadOnlyList<BusinessAccountDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Business accounts owned by one Snekl Account, newest first.</summary>
    Task<IReadOnlyList<BusinessAccountDto>> ListForPersonAsync(Guid personClusterId, CancellationToken ct = default);

    /// <summary>
    /// Moves a pending business account to approved. Approving an already approved account is a no-op;
    /// approving a rejected one is a conflict. Approval does not create a server: the owner does that later.
    /// </summary>
    Task<BusinessReviewResult> ApproveAsync(Guid businessAccountId, string reviewer, CancellationToken ct = default);

    /// <summary>Moves a pending business account to rejected, with the same idempotency rules as approve.</summary>
    Task<BusinessReviewResult> RejectAsync(Guid businessAccountId, string reviewer, CancellationToken ct = default);

    /// <summary>
    /// Business status for a member DID, for a trusted content host (signed channel). Uses the given
    /// business account when set (it must belong to the member), else the member's newest one.
    /// Null when the member has no business account.
    /// </summary>
    Task<BusinessHostingStatus?> GetHostingStatusAsync(string memberDid, Guid? businessAccountId, CancellationToken ct = default);

    /// <summary>
    /// Records the server a trusted content host created for an approved business. Idempotent: linking
    /// the same server again changes nothing. A business keeps at most one server, and a server belongs
    /// to at most one business (unique index on <c>ServerId</c>).
    /// </summary>
    Task<BusinessServerLinkResult> LinkServerAsync(BusinessHostingLinkCommand command, CancellationToken ct = default);
}

public sealed record BusinessServerLinkResult(
    BusinessServerLinkOutcome Outcome,
    BusinessHostingStatus? Business,
    string? Error = null);

public partial class BusinessAccountService : IBusinessAccountService
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;
    public const int MaxBusinessNameLength = 200;
    public const int MaxContactNameLength = 200;
    public const int MaxEmailLength = 254;
    public const int MaxWebsiteLength = 300;
    public const int MaxOtherServicesLength = 500;
    public const int MaxMessageLength = 2000;

    private readonly AccountDbContext _db;
    private readonly IAccountAuthService _auth;

    public BusinessAccountService(AccountDbContext db, IAccountAuthService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<BusinessAccountDto> SignUpAsync(
        BusinessSignupRequest request,
        string deviceKeyThumbprint,
        CancellationToken ct = default)
    {
        var input = Validate(request);

        if (await _db.PersonClusters.AnyAsync(c => c.CanonicalUsername == input.Username, ct))
            throw new InvalidOperationException("That username is taken. Choose another.");
        if (await _auth.EmailExistsAsync(input.Email, ct))
            throw new InvalidOperationException(
                "An account with that email already exists. Use a different email for the business account.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Same account path as POST /api/auth/register. No did:key is sent, so the auth server
        // generates one. The landing site does not keep the session or the generated key: the
        // contact signs in to the member app later with email + password.
        var session = await _auth.RegisterAsync(
            new RegisterRequest(input.Username, input.Email, request.Password, input.ContactName),
            deviceKeyThumbprint,
            ct);

        var business = new BusinessAccount
        {
            PersonClusterId = session.Person.Id,
            BusinessName = input.BusinessName,
            ContactName = input.ContactName,
            ContactEmail = input.Email,
            Website = input.Website,
            ServicesJson = JsonSerializer.Serialize(input.Services),
            OtherServices = input.OtherServices,
            Message = input.Message,
            Status = BusinessAccountStatuses.Pending
        };
        _db.BusinessAccounts.Add(business);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToDto(business, session.Person.CanonicalUsername, session.Person.EmailVerified);
    }

    public async Task<IReadOnlyList<BusinessAccountDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.BusinessAccounts
            .AsNoTracking()
            .Include(b => b.PersonCluster)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(b => ToDto(b)).ToList();
    }

    public async Task<IReadOnlyList<BusinessAccountDto>> ListForPersonAsync(Guid personClusterId, CancellationToken ct = default)
    {
        var rows = await _db.BusinessAccounts
            .AsNoTracking()
            .Include(b => b.PersonCluster)
            .Where(b => b.PersonClusterId == personClusterId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(b => ToDto(b)).ToList();
    }

    public Task<BusinessReviewResult> ApproveAsync(Guid businessAccountId, string reviewer, CancellationToken ct = default) =>
        ReviewAsync(businessAccountId, BusinessAccountStatuses.Approved, "approved", reviewer, ct);

    public Task<BusinessReviewResult> RejectAsync(Guid businessAccountId, string reviewer, CancellationToken ct = default) =>
        ReviewAsync(businessAccountId, BusinessAccountStatuses.Rejected, "rejected", reviewer, ct);

    private async Task<BusinessReviewResult> ReviewAsync(
        Guid businessAccountId,
        string targetStatus,
        string verb,
        string reviewer,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var by = string.IsNullOrWhiteSpace(reviewer) ? "operator" : reviewer.Trim();
        if (by.Length > 512)
            by = by[..512];

        // Conditional update: only a pending row changes, so two operators (or a double click)
        // cannot both "win", and a repeat call leaves the first review's time and reviewer alone.
        var changed = await _db.BusinessAccounts
            .Where(b => b.Id == businessAccountId && b.Status == BusinessAccountStatuses.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, targetStatus)
                .SetProperty(b => b.ReviewedAt, now)
                .SetProperty(b => b.ReviewedBy, by)
                .SetProperty(b => b.UpdatedAt, now), ct);

        var row = await _db.BusinessAccounts
            .AsNoTracking()
            .Include(b => b.PersonCluster)
            .FirstOrDefaultAsync(b => b.Id == businessAccountId, ct);

        if (row is null)
            return new BusinessReviewResult(BusinessReviewOutcome.NotFound, null, "Business account not found.");

        var dto = ToDto(row);
        if (changed == 1)
            return new BusinessReviewResult(BusinessReviewOutcome.Changed, dto);
        if (row.Status == targetStatus)
            return new BusinessReviewResult(BusinessReviewOutcome.Unchanged, dto);

        return new BusinessReviewResult(
            BusinessReviewOutcome.Conflict,
            dto,
            $"This business account is already {row.Status}. Only pending business accounts can be {verb}.");
    }

    public async Task<BusinessHostingStatus?> GetHostingStatusAsync(
        string memberDid,
        Guid? businessAccountId,
        CancellationToken ct = default)
    {
        var did = (memberDid ?? string.Empty).Trim();
        if (did.Length == 0)
            return null;

        var query = _db.BusinessAccounts
            .AsNoTracking()
            .Include(b => b.PersonCluster)
            .Where(b => b.PersonCluster!.Did == did);
        if (businessAccountId is Guid id)
            query = query.Where(b => b.Id == id);

        var row = await query.OrderByDescending(b => b.CreatedAt).FirstOrDefaultAsync(ct);
        return row is null ? null : ToHostingStatus(row);
    }

    public async Task<BusinessServerLinkResult> LinkServerAsync(
        BusinessHostingLinkCommand command,
        CancellationToken ct = default)
    {
        var did = (command.MemberDid ?? string.Empty).Trim();
        var serverDid = (command.ServerDid ?? string.Empty).Trim();
        var serverBaseUrl = (command.ServerBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (did.Length == 0 || command.BusinessAccountId == Guid.Empty || command.ServerId == Guid.Empty)
            throw new InvalidOperationException("Member DID, business account id, and server id are required.");
        if (serverDid.Length == 0 || serverDid.Length > 512)
            throw new InvalidOperationException("Server DID is required (at most 512 characters).");
        if (serverBaseUrl.Length > 512)
            throw new InvalidOperationException("Server base URL must be at most 512 characters.");

        var clusterId = await _db.PersonClusters
            .Where(p => p.Did == did)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (clusterId is null)
            return new BusinessServerLinkResult(BusinessServerLinkOutcome.NotFound, null, "Business account not found.");

        var now = DateTime.UtcNow;
        int changed;
        try
        {
            // Conditional update: only an approved business with no server yet changes, so a retry or a
            // race cannot replace a linked server. The unique index stops one server serving two businesses.
            changed = await _db.BusinessAccounts
                .Where(b => b.Id == command.BusinessAccountId
                    && b.PersonClusterId == clusterId.Value
                    && b.Status == BusinessAccountStatuses.Approved
                    && b.ServerId == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.ServerId, (Guid?)command.ServerId)
                    .SetProperty(b => b.ServerDid, serverDid)
                    .SetProperty(b => b.ServerBaseUrl, serverBaseUrl.Length == 0 ? null : serverBaseUrl)
                    .SetProperty(b => b.ServerLinkedAt, now)
                    .SetProperty(b => b.UpdatedAt, now), ct);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            return new BusinessServerLinkResult(
                BusinessServerLinkOutcome.ServerInUse,
                null,
                "That server is already linked to another business account.");
        }

        var row = await _db.BusinessAccounts
            .AsNoTracking()
            .Include(b => b.PersonCluster)
            .FirstOrDefaultAsync(b => b.Id == command.BusinessAccountId && b.PersonClusterId == clusterId.Value, ct);
        if (row is null)
            return new BusinessServerLinkResult(BusinessServerLinkOutcome.NotFound, null, "Business account not found.");

        var status = ToHostingStatus(row);
        if (changed == 1)
            return new BusinessServerLinkResult(BusinessServerLinkOutcome.Linked, status);
        if (row.ServerId == command.ServerId)
            return new BusinessServerLinkResult(BusinessServerLinkOutcome.AlreadyLinked, status);
        if (row.ServerId is not null)
            return new BusinessServerLinkResult(
                BusinessServerLinkOutcome.LinkedToOtherServer,
                status,
                "This business account already has a server.");
        return new BusinessServerLinkResult(
            BusinessServerLinkOutcome.NotApproved,
            status,
            $"This business account is {row.Status}. Only approved business accounts can have a server.");
    }

    private static bool IsUniqueViolation(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                return true;
        }
        return false;
    }

    private static BusinessHostingStatus ToHostingStatus(BusinessAccount business) =>
        new(
            business.Id,
            business.BusinessName,
            business.Status,
            business.PersonCluster?.Did ?? string.Empty,
            business.PersonCluster?.CanonicalUsername ?? string.Empty,
            business.ServerId,
            business.ServerDid,
            business.ServerBaseUrl);

    public sealed record ValidatedSignup(
        string BusinessName,
        string ContactName,
        string Email,
        string Username,
        IReadOnlyList<string> Services,
        string? OtherServices,
        string? Website,
        string? Message);

    /// <summary>Normalizes and checks a signup. Throws <see cref="InvalidOperationException"/> with a user-facing message.</summary>
    public static ValidatedSignup Validate(BusinessSignupRequest request)
    {
        var businessName = (request.BusinessName ?? string.Empty).Trim();
        if (businessName.Length < 2)
            throw new InvalidOperationException("Business name must be at least 2 characters.");
        if (businessName.Length > MaxBusinessNameLength)
            throw new InvalidOperationException($"Business name must be at most {MaxBusinessNameLength} characters.");

        var contactName = (request.ContactName ?? string.Empty).Trim();
        if (contactName.Length == 0)
            throw new InvalidOperationException("Contact name is required.");
        if (contactName.Length > MaxContactNameLength)
            throw new InvalidOperationException($"Contact name must be at most {MaxContactNameLength} characters.");

        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (!IsValidEmail(email))
            throw new InvalidOperationException("Enter a valid email address.");

        var username = (request.Username ?? string.Empty).Trim().ToLowerInvariant();
        if (!UsernamePattern().IsMatch(username))
            throw new InvalidOperationException(
                "Username must be 3 to 32 characters: lowercase letters, numbers, dashes, or underscores, starting with a letter or number.");

        var password = request.Password ?? string.Empty;
        if (password.Length < MinPasswordLength)
            throw new InvalidOperationException($"Password must be at least {MinPasswordLength} characters.");
        if (password.Length > MaxPasswordLength)
            throw new InvalidOperationException($"Password must be at most {MaxPasswordLength} characters.");

        var services = (request.Services ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
        if (services.Count == 0)
            throw new InvalidOperationException("Choose at least one service.");
        var unknown = services.FirstOrDefault(s => !BusinessServiceKeys.All.Contains(s));
        if (unknown is not null)
            throw new InvalidOperationException($"Unknown service: {unknown}.");

        var other = TrimToNull(request.OtherServices);
        if (services.Contains(BusinessServiceKeys.Other))
        {
            if (other is null)
                throw new InvalidOperationException("Tell us which other service you need.");
            if (other.Length > MaxOtherServicesLength)
                throw new InvalidOperationException($"Other service must be at most {MaxOtherServicesLength} characters.");
        }
        else
        {
            other = null;
        }

        var website = TrimToNull(request.Website);
        if (website is not null)
        {
            if (website.Length > MaxWebsiteLength
                || !Uri.TryCreate(website, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("Website must be a full http:// or https:// address.");
        }

        var message = TrimToNull(request.Message);
        if (message is not null && message.Length > MaxMessageLength)
            throw new InvalidOperationException($"Message must be at most {MaxMessageLength} characters.");

        return new ValidatedSignup(businessName, contactName, email, username, services, other, website, message);
    }

    private static bool IsValidEmail(string email)
    {
        if (email.Length == 0 || email.Length > MaxEmailLength || email.Contains(' '))
            return false;
        try
        {
            var parsed = new MailAddress(email);
            return parsed.Address == email && parsed.Host.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static BusinessAccountDto ToDto(BusinessAccount business) =>
        ToDto(
            business,
            business.PersonCluster?.CanonicalUsername ?? string.Empty,
            business.PersonCluster?.EmailVerifiedAt is not null);

    private static BusinessAccountDto ToDto(BusinessAccount business, string username, bool emailVerified) =>
        new(
            business.Id,
            business.BusinessName,
            business.ContactName,
            business.ContactEmail,
            business.Website,
            JsonSerializer.Deserialize<List<string>>(business.ServicesJson) ?? [],
            business.OtherServices,
            business.Message,
            business.Status,
            business.PersonClusterId,
            username,
            emailVerified,
            business.CreatedAt,
            business.ReviewedAt,
            business.ReviewedBy,
            business.ServerId,
            business.ServerDid,
            business.ServerBaseUrl,
            business.ServerLinkedAt);

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{2,31}$")]
    private static partial Regex UsernamePattern();
}
