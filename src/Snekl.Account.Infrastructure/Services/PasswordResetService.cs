using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;

using Snekl.Account.Infrastructure.Security;

namespace Snekl.Account.Infrastructure.Services;

public interface IPasswordResetService
{
    Task<MessageResponse> RequestResetAsync(ForgotPasswordRequest request, CancellationToken ct = default);
    Task<MessageResponse> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
}

public class PasswordResetService : IPasswordResetService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);
    private const int MinPasswordLength = 6;

    private readonly AccountDbContext _db;
    private readonly IAuthServerService _server;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;

    public PasswordResetService(
        AccountDbContext db,
        IAuthServerService server,
        IEmailSender email,
        IConfiguration config)
    {
        _db = db;
        _server = server;
        _email = email;
        _config = config;
    }

    public async Task<MessageResponse> RequestResetAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        const string responseMessage =
            "If an account exists with that email, a password reset link has been sent.";

        var email = request.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
            return new MessageResponse(responseMessage);

        var config = await _server.GetOrCreateAsync(ct);

        var identity = await _db.AuthIdentities
            .Include(i => i.PersonCluster)
            .FirstOrDefaultAsync(i =>
                i.AuthServerUrl == config.BaseUrl &&
                i.PersonCluster!.Email == email &&
                i.PersonCluster.EmailVerifiedAt != null, ct);

        if (identity is null)
            return new MessageResponse(responseMessage);

        var rawToken = SecureTokenHelper.GenerateToken();
        var tokenHash = SecureTokenHelper.HashToken(rawToken);

        var existingTokens = await _db.PasswordResetTokens
            .Where(t => t.AuthIdentityId == identity.Id && t.UsedAt == null)
            .ToListAsync(ct);

        foreach (var existing in existingTokens)
            existing.UsedAt = DateTime.UtcNow;

        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            AuthIdentityId = identity.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.Add(TokenLifetime)
        });

        await _db.SaveChangesAsync(ct);

        var frontendBase = (_config["Frontend:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
        var resetUrl = $"{frontendBase}/reset-password?token={Uri.EscapeDataString(rawToken)}";
        await _email.SendPasswordResetAsync(email, resetUrl, ct);

        return new MessageResponse(responseMessage);
    }

    public async Task<MessageResponse> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            throw new InvalidOperationException("Invalid or expired reset token.");

        if (request.NewPassword.Length < MinPasswordLength)
            throw new InvalidOperationException($"Password must be at least {MinPasswordLength} characters.");

        var tokenHash = SecureTokenHelper.HashToken(request.Token.Trim());
        var now = DateTime.UtcNow;

        var resetToken = await _db.PasswordResetTokens
            .Include(t => t.AuthIdentity)
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash &&
                t.UsedAt == null &&
                t.ExpiresAt > now, ct);

        if (resetToken?.AuthIdentity is null)
            throw new InvalidOperationException("Invalid or expired reset token.");

        resetToken.UsedAt = now;
        resetToken.AuthIdentity.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

        await _db.SaveChangesAsync(ct);

        return new MessageResponse("Password updated. You can log in with your new password.");
    }
}
