using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;
using Snekl.Account.Infrastructure.Security;

namespace Snekl.Account.Infrastructure.Services;

public enum TwoFactorProof
{
    Accepted,
    Missing,
    Invalid
}

public enum TwoFactorLoginStatus
{
    Success,
    InvalidCode,
    InvalidChallenge
}

public sealed record TwoFactorLoginResult(TwoFactorLoginStatus Status, RingSessionDto? Session);

public interface ITwoFactorService
{
    Task<TwoFactorStatusDto> GetStatusAsync(Guid identityId, CancellationToken ct = default);
    Task<TwoFactorSetupDto> BeginSetupAsync(Guid identityId, CancellationToken ct = default);
    Task<TwoFactorRecoveryCodesDto> ConfirmSetupAsync(Guid identityId, string code, CancellationToken ct = default);
    Task DisableAsync(Guid identityId, string password, string code, CancellationToken ct = default);
    Task<TwoFactorRecoveryCodesDto> RegenerateRecoveryCodesAsync(Guid identityId, string password, string code, CancellationToken ct = default);
    Task<AccountLoginResult> BeginLoginChallengeAsync(AuthIdentity identity, string deviceKeyThumbprint, CancellationToken ct = default);
    Task<TwoFactorLoginResult> CompleteLoginAsync(string challengeToken, string code, CancellationToken ct = default);
    Task<TwoFactorProof> CheckCodeAsync(AuthIdentity identity, string? code, bool allowRecovery, CancellationToken ct = default);
    Task InvalidateLoginChallengesAsync(Guid identityId, CancellationToken ct = default);
}

public class TwoFactorService : ITwoFactorService
{
    private const int RecoveryCodeCount = 10;
    private const int MaxChallengeAttempts = 5;
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
    private const string RecoveryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly AccountDbContext _db;
    private readonly IAuthServerService _server;
    private readonly IRingLoginService _ring;

    public TwoFactorService(AccountDbContext db, IAuthServerService server, IRingLoginService ring)
    {
        _db = db;
        _server = server;
        _ring = ring;
    }

    public async Task<TwoFactorStatusDto> GetStatusAsync(Guid identityId, CancellationToken ct = default)
    {
        var identity = await RequireIdentityAsync(identityId, ct);
        var remaining = await _db.TwoFactorRecoveryCodes
            .CountAsync(c => c.AuthIdentityId == identity.Id && c.UsedAt == null, ct);
        return new TwoFactorStatusDto(IsEnabled(identity), remaining);
    }

    public async Task<TwoFactorSetupDto> BeginSetupAsync(Guid identityId, CancellationToken ct = default)
    {
        var identity = await RequireIdentityAsync(identityId, ct);
        if (identity.PasswordHash is null)
            throw new InvalidOperationException("This account cannot use an authenticator app.");
        if (IsEnabled(identity))
            throw new InvalidOperationException("Turn off two-factor authentication before setting it up again.");

        var secret = Totp.GenerateSecret();
        var key = await ProtectionKeyAsync(ct);
        identity.TotpPendingSecretProtected = SecretProtector.Protect(secret, key);
        await _db.SaveChangesAsync(ct);

        var email = identity.PersonCluster?.Email ?? identity.Id.ToString();
        var config = await _server.GetOrCreateAsync(ct);
        return new TwoFactorSetupDto(secret, Totp.BuildOtpAuthUri(config.Name, email, secret));
    }

    public async Task<TwoFactorRecoveryCodesDto> ConfirmSetupAsync(Guid identityId, string code, CancellationToken ct = default)
    {
        var identity = await RequireIdentityAsync(identityId, ct);
        if (string.IsNullOrEmpty(identity.TotpPendingSecretProtected))
            throw new InvalidOperationException("Start authenticator setup before confirming a code.");
        if (IsEnabled(identity))
            throw new InvalidOperationException("Two-factor authentication is already on.");

        var key = await ProtectionKeyAsync(ct);
        var secret = Unprotect(identity.TotpPendingSecretProtected, key);
        if (!Totp.TryMatch(secret, code, DateTimeOffset.UtcNow, null, out var step))
            throw new InvalidOperationException("Invalid authentication code.");

        identity.TotpSecretProtected = identity.TotpPendingSecretProtected;
        identity.TotpPendingSecretProtected = null;
        identity.TotpEnabledAt = DateTime.UtcNow;
        identity.TotpLastAcceptedStep = step;

        var existing = await _db.TwoFactorRecoveryCodes
            .Where(c => c.AuthIdentityId == identity.Id)
            .ToListAsync(ct);
        _db.TwoFactorRecoveryCodes.RemoveRange(existing);
        var codes = AddRecoveryCodes(identity.Id, key);
        await _db.SaveChangesAsync(ct);
        return new TwoFactorRecoveryCodesDto(codes);
    }

    public async Task DisableAsync(Guid identityId, string password, string code, CancellationToken ct = default)
    {
        var identity = await RequireIdentityAsync(identityId, ct);
        if (!IsEnabled(identity))
            throw new InvalidOperationException("Two-factor authentication is already off.");
        RequirePassword(identity, password);

        var proof = await CheckCodeAsync(identity, code, allowRecovery: true, ct);
        if (proof != TwoFactorProof.Accepted)
            throw new InvalidOperationException("Invalid authentication code.");

        identity.TotpSecretProtected = null;
        identity.TotpPendingSecretProtected = null;
        identity.TotpEnabledAt = null;
        identity.TotpLastAcceptedStep = null;

        var codes = await _db.TwoFactorRecoveryCodes
            .Where(c => c.AuthIdentityId == identity.Id)
            .ToListAsync(ct);
        _db.TwoFactorRecoveryCodes.RemoveRange(codes);
        await InvalidateLoginChallengesAsync(identity.Id, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<TwoFactorRecoveryCodesDto> RegenerateRecoveryCodesAsync(
        Guid identityId,
        string password,
        string code,
        CancellationToken ct = default)
    {
        var identity = await RequireIdentityAsync(identityId, ct);
        if (!IsEnabled(identity))
            throw new InvalidOperationException("Turn on two-factor authentication first.");
        RequirePassword(identity, password);

        var proof = await CheckCodeAsync(identity, code, allowRecovery: false, ct);
        if (proof != TwoFactorProof.Accepted)
            throw new InvalidOperationException("Invalid authentication code. Enter the current code from your authenticator app.");

        var key = await ProtectionKeyAsync(ct);
        var existing = await _db.TwoFactorRecoveryCodes
            .Where(c => c.AuthIdentityId == identity.Id)
            .ToListAsync(ct);
        _db.TwoFactorRecoveryCodes.RemoveRange(existing);
        var codes = AddRecoveryCodes(identity.Id, key);
        await _db.SaveChangesAsync(ct);
        return new TwoFactorRecoveryCodesDto(codes);
    }

    public async Task<AccountLoginResult> BeginLoginChallengeAsync(AuthIdentity identity, string deviceKeyThumbprint, CancellationToken ct = default)
    {
        await InvalidateLoginChallengesAsync(identity.Id, ct);
        var token = SecureTokenHelper.GenerateToken();
        var expires = DateTime.UtcNow.Add(ChallengeLifetime);
        _db.TwoFactorChallenges.Add(new TwoFactorChallenge
        {
            AuthIdentityId = identity.Id,
            TokenHash = SecureTokenHelper.HashToken(token),
            DeviceKeyThumbprint = deviceKeyThumbprint,
            ExpiresAt = expires
        });
        await _db.SaveChangesAsync(ct);
        return AccountLoginResult.NeedsTwoFactor(token, expires);
    }

    public async Task<TwoFactorLoginResult> CompleteLoginAsync(string challengeToken, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(challengeToken) || string.IsNullOrWhiteSpace(code))
            return new TwoFactorLoginResult(TwoFactorLoginStatus.InvalidChallenge, null);

        var now = DateTime.UtcNow;
        var hash = SecureTokenHelper.HashToken(challengeToken.Trim());
        var challenge = await _db.TwoFactorChallenges
            .FirstOrDefaultAsync(c => c.TokenHash == hash, ct);

        if (challenge is null || challenge.UsedAt is not null || challenge.ExpiresAt <= now || challenge.FailedAttempts >= MaxChallengeAttempts)
            return new TwoFactorLoginResult(TwoFactorLoginStatus.InvalidChallenge, null);

        var identity = await _db.AuthIdentities
            .Include(i => i.PersonCluster)
            .FirstOrDefaultAsync(i => i.Id == challenge.AuthIdentityId, ct);

        if (identity?.PersonCluster is null || !IsEnabled(identity))
        {
            challenge.UsedAt = now;
            await _db.SaveChangesAsync(ct);
            return new TwoFactorLoginResult(TwoFactorLoginStatus.InvalidChallenge, null);
        }

        var proof = await CheckCodeAsync(identity, code, allowRecovery: true, ct);
        if (proof != TwoFactorProof.Accepted)
        {
            challenge.FailedAttempts++;
            if (challenge.FailedAttempts >= MaxChallengeAttempts)
                challenge.UsedAt = now;
            await _db.SaveChangesAsync(ct);
            return new TwoFactorLoginResult(TwoFactorLoginStatus.InvalidCode, null);
        }

        if (string.IsNullOrWhiteSpace(challenge.DeviceKeyThumbprint))
        {
            challenge.UsedAt = now;
            await _db.SaveChangesAsync(ct);
            return new TwoFactorLoginResult(TwoFactorLoginStatus.InvalidChallenge, null);
        }

        challenge.UsedAt = now;
        var config = await _server.GetOrCreateAsync(ct);
        var session = await _ring.CreateRingSessionAsync(
            identity, identity.PersonCluster, config, ct, DateTimeOffset.UtcNow, challenge.DeviceKeyThumbprint);
        return new TwoFactorLoginResult(TwoFactorLoginStatus.Success, session);
    }

    public async Task<TwoFactorProof> CheckCodeAsync(
        AuthIdentity identity,
        string? code,
        bool allowRecovery,
        CancellationToken ct = default)
    {
        if (!IsEnabled(identity))
            return TwoFactorProof.Accepted;

        if (string.IsNullOrWhiteSpace(code))
            return TwoFactorProof.Missing;

        var normalized = Normalize(code);
        if (normalized.Length == Totp.Digits && normalized.All(char.IsDigit))
        {
            var key = await ProtectionKeyAsync(ct);
            var secret = Unprotect(identity.TotpSecretProtected!, key);
            if (!Totp.TryMatch(secret, normalized, DateTimeOffset.UtcNow, identity.TotpLastAcceptedStep, out var step))
                return TwoFactorProof.Invalid;

            identity.TotpLastAcceptedStep = step;
            return TwoFactorProof.Accepted;
        }

        if (!allowRecovery || normalized.Length != 8)
            return TwoFactorProof.Invalid;

        var protectionKey = await ProtectionKeyAsync(ct);
        var hashed = HashRecoveryCode(normalized, protectionKey);
        var hashBytes = Encoding.ASCII.GetBytes(hashed);
        var unused = await _db.TwoFactorRecoveryCodes
            .Where(c => c.AuthIdentityId == identity.Id && c.UsedAt == null)
            .ToListAsync(ct);

        TwoFactorRecoveryCode? match = null;
        foreach (var recovery in unused)
        {
            var stored = Encoding.ASCII.GetBytes(recovery.CodeHash);
            if (stored.Length == hashBytes.Length && CryptographicOperations.FixedTimeEquals(stored, hashBytes))
                match ??= recovery;
        }

        if (match is null)
            return TwoFactorProof.Invalid;

        match.UsedAt = DateTime.UtcNow;
        return TwoFactorProof.Accepted;
    }

    public async Task InvalidateLoginChallengesAsync(Guid identityId, CancellationToken ct = default)
    {
        var open = await _db.TwoFactorChallenges
            .Where(c => c.AuthIdentityId == identityId && c.UsedAt == null)
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var challenge in open)
            challenge.UsedAt = now;
    }

    private async Task<AuthIdentity> RequireIdentityAsync(Guid identityId, CancellationToken ct)
    {
        return await _db.AuthIdentities
            .Include(i => i.PersonCluster)
            .FirstOrDefaultAsync(i => i.Id == identityId, ct)
            ?? throw new InvalidOperationException("Identity not found.");
    }

    private async Task<string> ProtectionKeyAsync(CancellationToken ct)
    {
        var config = await _server.GetOrCreateAsync(ct);
        if (string.IsNullOrWhiteSpace(config.TotpProtectionKey))
            throw new InvalidOperationException("Two-factor protection key is not configured.");
        return config.TotpProtectionKey;
    }

    private static bool IsEnabled(AuthIdentity identity) =>
        identity.TotpEnabledAt is not null && !string.IsNullOrEmpty(identity.TotpSecretProtected);

    private static void RequirePassword(AuthIdentity identity, string password)
    {
        if (identity.PasswordHash is null || string.IsNullOrWhiteSpace(password) ||
            !BCrypt.Net.BCrypt.Verify(password, identity.PasswordHash))
            throw new InvalidOperationException("Invalid password.");
    }

    private static string Unprotect(string payload, string key)
    {
        try
        {
            return SecretProtector.Unprotect(payload, key);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Two-factor secret could not be read.");
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Two-factor secret could not be read.");
        }
    }

    private List<string> AddRecoveryCodes(Guid identityId, string protectionKey)
    {
        var codes = new List<string>(RecoveryCodeCount);
        for (var i = 0; i < RecoveryCodeCount; i++)
        {
            var plaintext = NewRecoveryCode();
            codes.Add(plaintext);
            _db.TwoFactorRecoveryCodes.Add(new TwoFactorRecoveryCode
            {
                AuthIdentityId = identityId,
                CodeHash = HashRecoveryCode(Normalize(plaintext), protectionKey)
            });
        }

        return codes;
    }

    private static string NewRecoveryCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(8);
        var chars = new char[8];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = RecoveryAlphabet[bytes[i] % RecoveryAlphabet.Length];
        return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}";
    }

    private static string HashRecoveryCode(string normalized, string protectionKey)
    {
        var hash = HMACSHA256.HashData(SecretProtector.DeriveKey(protectionKey), Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Normalize(string code)
    {
        var trimmed = code.Trim();
        if (trimmed.Length > 64)
            trimmed = trimmed[..64];
        var chars = trimmed.Where(char.IsLetterOrDigit).ToArray();
        return new string(chars).ToUpperInvariant();
    }
}
