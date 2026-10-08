using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Snekl.Hub.Domain.Dtos;
using Snekl.Hub.Domain.Entities;
using Snekl.Hub.Infrastructure.Data;
using Snekl.Hub.Infrastructure.Security;

namespace Snekl.Hub.Infrastructure.Services;

public interface IHubOperatorService
{
    Task<HubOperatorStatusDto> GetStatusAsync(string did, CancellationToken ct = default);
    Task<HubOperatorStatusDto> GetStatusAsync(string did, string? username, CancellationToken ct = default);
    Task<HubAdminAuthResponse> ElevateAsync(string did, string username, HubOperatorLoginRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<HubOperatorDto>> ListAsync(CancellationToken ct = default);
    Task<HubOperatorDto> InviteAsync(Guid invitedByOperatorId, InviteHubOperatorRequest request, CancellationToken ct = default);
    Task RevokeAsync(Guid operatorId, CancellationToken ct = default);
}

public class HubOperatorService : IHubOperatorService
{
    private readonly HubDbContext _db;
    private readonly IConfiguration _config;

    public HubOperatorService(HubDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public Task<HubOperatorStatusDto> GetStatusAsync(string did, CancellationToken ct = default) =>
        GetStatusAsync(did, null, ct);

    public async Task<HubOperatorStatusDto> GetStatusAsync(string did, string? username, CancellationToken ct = default)
    {
        if (!await _db.HubOperators.AnyAsync(ct))
            return new HubOperatorStatusDto(HubOperatorStates.Unclaimed);

        var op = await FindAsync(did, username, ct);
        if (op is null)
            return new HubOperatorStatusDto(HubOperatorStates.Forbidden);
        if (!HasPassword(op))
            return new HubOperatorStatusDto(HubOperatorStates.SetupRequired);
        return new HubOperatorStatusDto(HubOperatorStates.Ready);
    }

    public async Task<HubAdminAuthResponse> ElevateAsync(
        string did,
        string username,
        HubOperatorLoginRequest request,
        CancellationToken ct = default)
    {
        did = NormalizeDid(did);
        username = string.IsNullOrWhiteSpace(username) ? did : username.Trim();

        if (!await _db.HubOperators.AnyAsync(ct))
        {
            var password = RequireNewPassword(request, "Choose an admin password to claim this hub.");
            var claimed = new HubOperator
            {
                Did = did,
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
            };
            _db.HubOperators.Add(claimed);
            await _db.SaveChangesAsync(ct);
            return new HubAdminAuthResponse(GenerateToken(claimed), ToDto(claimed));
        }

        var op = await FindAsync(did, username, ct)
            ?? throw new UnauthorizedAccessException("You are not an operator on this hub.");

        if (op.Did != did && (op.Did.StartsWith("did:snekl:pending:", StringComparison.Ordinal) || op.Username == username))
            op.Did = did;

        if (!HasPassword(op))
        {
            var password = RequireNewPassword(request, "Set an admin password for this hub.");
            op.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            if (!string.IsNullOrWhiteSpace(username))
                op.Username = username;
            await _db.SaveChangesAsync(ct);
            return new HubAdminAuthResponse(GenerateToken(op), ToDto(op));
        }

        if (!string.IsNullOrWhiteSpace(request.NewAdminPassword))
            throw new InvalidOperationException("This operator already has an admin password on this hub.");

        if (string.IsNullOrWhiteSpace(request.AdminPassword)
            || !BCrypt.Net.BCrypt.Verify(request.AdminPassword, op.PasswordHash))
            throw new UnauthorizedAccessException("Invalid admin password.");

        return new HubAdminAuthResponse(GenerateToken(op), ToDto(op));
    }

    public async Task<IReadOnlyList<HubOperatorDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.HubOperators.AsNoTracking().OrderBy(o => o.CreatedAt).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<HubOperatorDto> InviteAsync(
        Guid invitedByOperatorId,
        InviteHubOperatorRequest request,
        CancellationToken ct = default)
    {
        var username = request.Username?.Trim();
        var did = string.IsNullOrWhiteSpace(request.Did) ? null : NormalizeDid(request.Did);
        if (string.IsNullOrWhiteSpace(did) && string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("Username or DID is required.");

        did ??= $"did:snekl:pending:{username!.ToLowerInvariant()}";
        username = string.IsNullOrWhiteSpace(username) ? did : username;

        if (await _db.HubOperators.AnyAsync(o => o.Did == did || o.Username == username, ct))
            throw new InvalidOperationException("That person is already an operator on this hub.");

        var invite = new HubOperator
        {
            Did = did,
            Username = username,
            PasswordHash = null,
            InvitedByOperatorId = invitedByOperatorId
        };
        _db.HubOperators.Add(invite);
        await _db.SaveChangesAsync(ct);
        return ToDto(invite);
    }

    public async Task RevokeAsync(Guid operatorId, CancellationToken ct = default)
    {
        var op = await _db.HubOperators.FirstOrDefaultAsync(o => o.Id == operatorId, ct)
            ?? throw new InvalidOperationException("Operator not found.");

        if (await _db.HubOperators.CountAsync(ct) <= 1)
            throw new InvalidOperationException("Cannot remove the last operator.");

        _db.HubOperators.Remove(op);
        await _db.SaveChangesAsync(ct);
    }

    private Task<HubOperator?> FindAsync(string did, string? username, CancellationToken ct)
    {
        did = NormalizeDid(did);
        var user = username?.Trim();
        var pending = string.IsNullOrWhiteSpace(user) ? null : $"did:snekl:pending:{user.ToLowerInvariant()}";
        return _db.HubOperators.FirstOrDefaultAsync(
            o => o.Did == did
                 || (pending != null && o.Did == pending)
                 || (user != null && o.Username.ToLower() == user.ToLower()),
            ct);
    }

    private static bool HasPassword(HubOperator op) => !string.IsNullOrEmpty(op.PasswordHash);

    private static string NormalizeDid(string did)
    {
        var value = did.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("DID is required.");
        return value;
    }

    private static string RequireNewPassword(HubOperatorLoginRequest request, string missingMessage)
    {
        var password = request.NewAdminPassword?.Trim();
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(missingMessage);
        if (password.Length < 8)
            throw new InvalidOperationException("Admin password must be at least 8 characters.");
        return password;
    }

    private string GenerateToken(HubOperator op)
    {
        var signingKey = _config["Hub:AdminSigningKey"]
            ?? "snekl-hub-admin-dev-signing-key-change-in-production-min-32";
        var issuer = (_config["Hub:BaseUrl"] ?? "http://localhost:5200").TrimEnd('/');
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiryHours = int.Parse(_config["Hub:AdminTokenExpiryHours"] ?? "8");

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: HubAdminAuth.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, op.Id.ToString()),
                new Claim("did", op.Did),
                new Claim("username", op.Username),
                new Claim(HubAdminAuth.RoleClaim, HubAdminAuth.Role)
            ],
            expires: DateTime.UtcNow.AddHours(expiryHours),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static HubOperatorDto ToDto(HubOperator op) =>
        new(op.Id, op.Did, op.Username, HasPassword(op), op.CreatedAt);
}
