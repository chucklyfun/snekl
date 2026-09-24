using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;
using Snekl.Account.Infrastructure.Security;

namespace Snekl.Account.Infrastructure.Services;

public interface IAuthOperatorService
{
    Task<AuthOperatorStatusDto> GetStatusAsync(string did, string? username, CancellationToken ct = default);
    Task<AuthAdminAuthResponse> ElevateAsync(string did, string username, AuthOperatorLoginRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AuthOperatorDto>> ListAsync(CancellationToken ct = default);
    Task<AuthOperatorDto> InviteAsync(Guid invitedByOperatorId, string username, CancellationToken ct = default);
    Task RevokeAsync(Guid operatorId, CancellationToken ct = default);
}

public class AuthOperatorService : IAuthOperatorService
{
    private readonly AccountDbContext _db;
    private readonly IConfiguration _config;

    public AuthOperatorService(AccountDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<AuthOperatorStatusDto> GetStatusAsync(string did, string? username, CancellationToken ct = default)
    {
        if (!await _db.AuthOperators.AnyAsync(ct))
            return new AuthOperatorStatusDto(AuthOperatorStates.Unclaimed);

        var op = await FindAsync(did, username, ct);
        if (op is null)
            return new AuthOperatorStatusDto(AuthOperatorStates.Forbidden);
        if (string.IsNullOrEmpty(op.PasswordHash))
            return new AuthOperatorStatusDto(AuthOperatorStates.SetupRequired);
        return new AuthOperatorStatusDto(AuthOperatorStates.Ready);
    }

    public async Task<AuthAdminAuthResponse> ElevateAsync(
        string did,
        string username,
        AuthOperatorLoginRequest request,
        CancellationToken ct = default)
    {
        did = did.Trim();
        username = string.IsNullOrWhiteSpace(username) ? did : username.Trim();

        if (!await _db.AuthOperators.AnyAsync(ct))
        {
            var password = RequireNewPassword(request, "Choose an admin password to claim this auth server.");
            var claimed = new AuthOperator
            {
                Did = did,
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
            };
            _db.AuthOperators.Add(claimed);
            await _db.SaveChangesAsync(ct);
            return new AuthAdminAuthResponse(GenerateToken(claimed), ToDto(claimed));
        }

        var op = await FindAsync(did, username, ct)
            ?? throw new UnauthorizedAccessException("You are not an operator on this auth server.");

        if (op.Did != did)
        {
            op.Did = did;
            op.Username = username;
        }

        if (string.IsNullOrEmpty(op.PasswordHash))
        {
            var password = RequireNewPassword(request, "Set an admin password for this auth server.");
            op.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            await _db.SaveChangesAsync(ct);
            return new AuthAdminAuthResponse(GenerateToken(op), ToDto(op));
        }

        if (!string.IsNullOrWhiteSpace(request.NewAdminPassword))
            throw new InvalidOperationException("This operator already has an admin password on this auth server.");

        if (string.IsNullOrWhiteSpace(request.AdminPassword)
            || !BCrypt.Net.BCrypt.Verify(request.AdminPassword, op.PasswordHash))
            throw new UnauthorizedAccessException("Invalid admin password.");

        return new AuthAdminAuthResponse(GenerateToken(op), ToDto(op));
    }

    public async Task<IReadOnlyList<AuthOperatorDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.AuthOperators.AsNoTracking().OrderBy(o => o.CreatedAt).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<AuthOperatorDto> InviteAsync(Guid invitedByOperatorId, string username, CancellationToken ct = default)
    {
        username = username.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("Username is required.");

        var cluster = await _db.PersonClusters.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CanonicalUsername == username, ct)
            ?? throw new InvalidOperationException("No account with that username on this auth server.");

        if (await _db.AuthOperators.AnyAsync(o => o.Did == cluster.Did || o.Username == username, ct))
            throw new InvalidOperationException("That user is already an operator on this auth server.");

        var invite = new AuthOperator
        {
            Did = cluster.Did,
            Username = cluster.CanonicalUsername,
            PasswordHash = null,
            InvitedByOperatorId = invitedByOperatorId
        };
        _db.AuthOperators.Add(invite);
        await _db.SaveChangesAsync(ct);
        return ToDto(invite);
    }

    public async Task RevokeAsync(Guid operatorId, CancellationToken ct = default)
    {
        var op = await _db.AuthOperators.FirstOrDefaultAsync(o => o.Id == operatorId, ct)
            ?? throw new InvalidOperationException("Operator not found.");

        if (await _db.AuthOperators.CountAsync(ct) <= 1)
            throw new InvalidOperationException("Cannot remove the last operator.");

        _db.AuthOperators.Remove(op);
        await _db.SaveChangesAsync(ct);
    }

    private Task<AuthOperator?> FindAsync(string did, string? username, CancellationToken ct)
    {
        var pending = string.IsNullOrWhiteSpace(username)
            ? null
            : $"did:snekl:pending:{username.Trim().ToLowerInvariant()}";
        var user = username?.Trim();
        return _db.AuthOperators.FirstOrDefaultAsync(
            o => o.Did == did
                 || (user != null && o.Username.ToLower() == user.ToLower())
                 || (pending != null && o.Did == pending),
            ct);
    }

    private static string RequireNewPassword(AuthOperatorLoginRequest request, string missingMessage)
    {
        var password = request.NewAdminPassword?.Trim();
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(missingMessage);
        if (password.Length < 8)
            throw new InvalidOperationException("Admin password must be at least 8 characters.");
        return password;
    }

    private string GenerateToken(AuthOperator op)
    {
        var signingKey = _config["Auth:AdminSigningKey"]
            ?? "snekl-auth-admin-dev-signing-key-change-in-production-min-32";
        var issuer = (_config["Auth:BaseUrl"] ?? "http://localhost:5100").TrimEnd('/');
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiryHours = int.Parse(_config["Auth:AdminTokenExpiryHours"] ?? "8");

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: AuthAdminAuth.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, op.Id.ToString()),
                new Claim("did", op.Did),
                new Claim("username", op.Username),
                new Claim(AuthAdminAuth.RoleClaim, AuthAdminAuth.Role)
            ],
            expires: DateTime.UtcNow.AddHours(expiryHours),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static AuthOperatorDto ToDto(AuthOperator op) =>
        new(op.Id, op.Did, op.Username, !string.IsNullOrEmpty(op.PasswordHash), op.CreatedAt);
}
