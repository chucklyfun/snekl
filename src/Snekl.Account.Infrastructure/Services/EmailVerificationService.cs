using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;
using Snekl.Account.Infrastructure.Security;

namespace Snekl.Account.Infrastructure.Services;

public interface IEmailVerificationService
{
    bool AutoVerifyEnabled { get; }
    Task EnsureVerifiedOrSendAsync(PersonCluster cluster, CancellationToken ct = default);
    Task<MessageResponse> VerifyAsync(string token, CancellationToken ct = default);
    Task<MessageResponse> ResendAsync(Guid clusterId, CancellationToken ct = default);
}

public class EmailVerificationService : IEmailVerificationService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);

    private readonly AccountDbContext _db;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;

    public EmailVerificationService(AccountDbContext db, IEmailSender email, IConfiguration config)
    {
        _db = db;
        _email = email;
        _config = config;
    }

    public bool AutoVerifyEnabled => _config.GetValue("Email:AutoVerify", false);

    public async Task EnsureVerifiedOrSendAsync(PersonCluster cluster, CancellationToken ct = default)
    {
        if (AutoVerifyEnabled)
        {
            await MarkVerifiedAsync(cluster, ct);
            return;
        }

        if (cluster.EmailVerifiedAt is not null)
            return;

        await SendVerificationAsync(cluster, ct);
    }

    public async Task<MessageResponse> VerifyAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Invalid or expired verification token.");

        var tokenHash = SecureTokenHelper.HashToken(token.Trim());
        var now = DateTime.UtcNow;

        var verificationToken = await _db.EmailVerificationTokens
            .Include(t => t.PersonCluster)
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash &&
                t.UsedAt == null &&
                t.ExpiresAt > now, ct);

        if (verificationToken?.PersonCluster is null)
            throw new InvalidOperationException("Invalid or expired verification token.");

        verificationToken.UsedAt = now;
        await MarkVerifiedAsync(verificationToken.PersonCluster, ct);

        return new MessageResponse("Email verified. You're all set.");
    }

    public async Task<MessageResponse> ResendAsync(Guid clusterId, CancellationToken ct = default)
    {
        var cluster = await _db.PersonClusters.FirstOrDefaultAsync(c => c.Id == clusterId, ct)
            ?? throw new InvalidOperationException("Person cluster not found.");

        if (cluster.EmailVerifiedAt is not null)
            return new MessageResponse("Email is already verified.");

        await SendVerificationAsync(cluster, ct);
        return new MessageResponse("Verification email sent.");
    }

    private async Task SendVerificationAsync(PersonCluster cluster, CancellationToken ct)
    {
        var rawToken = SecureTokenHelper.GenerateToken();
        var tokenHash = SecureTokenHelper.HashToken(rawToken);

        var existingTokens = await _db.EmailVerificationTokens
            .Where(t => t.PersonClusterId == cluster.Id && t.UsedAt == null)
            .ToListAsync(ct);

        foreach (var existing in existingTokens)
            existing.UsedAt = DateTime.UtcNow;

        _db.EmailVerificationTokens.Add(new EmailVerificationToken
        {
            PersonClusterId = cluster.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.Add(TokenLifetime)
        });

        await _db.SaveChangesAsync(ct);

        var frontendBase = (_config["Frontend:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
        var verifyUrl = $"{frontendBase}/verify-email?token={Uri.EscapeDataString(rawToken)}";
        await _email.SendEmailVerificationAsync(cluster.Email, verifyUrl, ct);
    }

    private async Task MarkVerifiedAsync(PersonCluster cluster, CancellationToken ct)
    {
        var email = cluster.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Email is required.");

        if (await _db.PersonClusters.AnyAsync(
                c => c.Email == email && c.EmailVerifiedAt != null && c.Id != cluster.Id, ct))
            throw new InvalidOperationException("Email is already verified on another account.");

        cluster.Email = email;
        cluster.EmailVerifiedAt = DateTime.UtcNow;
        cluster.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
