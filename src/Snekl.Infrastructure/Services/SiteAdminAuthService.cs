using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Security;

namespace Snekl.Infrastructure.Services;

public interface ISiteAdminAuthService
{
    Task<OperatorStatusDto> GetStatusAsync(Guid userId, CancellationToken ct = default);
    Task<SiteAdminAuthResponse> ElevateAsync(Guid userId, OperatorLoginRequest request, CancellationToken ct = default);
    Task<SiteAdminDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SiteAdminDto>> ListAsync(CancellationToken ct = default);
    Task<SiteAdminDto> InviteAsync(Guid invitedByAdminId, string username, CancellationToken ct = default);
    Task RevokeAsync(Guid operatorId, CancellationToken ct = default);
}

public class SiteAdminAuthService : ISiteAdminAuthService
{
    private readonly SneklDbContext _db;
    private readonly IConfiguration _config;

    public SiteAdminAuthService(SneklDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<OperatorStatusDto> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        if (!await _db.SiteAdmins.AnyAsync(ct))
            return new OperatorStatusDto(OperatorStates.Unclaimed);

        var admin = await _db.SiteAdmins.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (admin is null)
            return new OperatorStatusDto(OperatorStates.Forbidden);
        if (!HasPassword(admin))
            return new OperatorStatusDto(OperatorStates.SetupRequired);
        return new OperatorStatusDto(OperatorStates.Ready);
    }

    public async Task<SiteAdminAuthResponse> ElevateAsync(
        Guid userId,
        OperatorLoginRequest request,
        CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedAccessException("User not found.");

        var any = await _db.SiteAdmins.AnyAsync(ct);
        if (!any)
        {
            var password = RequireNewPassword(request, "Choose an admin password to claim this server.");
            var claimed = new SiteAdmin
            {
                UserId = user.Id,
                Did = string.IsNullOrWhiteSpace(user.Did) ? null : user.Did,
                Username = user.Username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
            };
            _db.SiteAdmins.Add(claimed);
            await EnsureServerOwnerAsync(user.Id, ct);
            await EnsureSuperUserAsync(user.Id, claimed.Id, ct);
            await _db.SaveChangesAsync(ct);
            return new SiteAdminAuthResponse(GenerateToken(claimed), ToDto(claimed));
        }

        var admin = await _db.SiteAdmins.FirstOrDefaultAsync(a => a.UserId == userId, ct)
            ?? throw new UnauthorizedAccessException("You are not an operator on this server.");

        if (!HasPassword(admin))
        {
            var password = RequireNewPassword(request, "Set an admin password for this server.");
            admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            await EnsureSuperUserAsync(user.Id, admin.Id, ct);
            await _db.SaveChangesAsync(ct);
            return new SiteAdminAuthResponse(GenerateToken(admin), ToDto(admin));
        }

        if (!string.IsNullOrWhiteSpace(request.NewAdminPassword))
            throw new InvalidOperationException("This operator already has an admin password on this server.");

        if (string.IsNullOrWhiteSpace(request.AdminPassword)
            || !BCrypt.Net.BCrypt.Verify(request.AdminPassword, admin.PasswordHash))
            throw new UnauthorizedAccessException("Invalid admin password.");

        await EnsureSuperUserAsync(user.Id, admin.Id, ct);
        await _db.SaveChangesAsync(ct);
        return new SiteAdminAuthResponse(GenerateToken(admin), ToDto(admin));
    }

    public async Task<SiteAdminDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var admin = await _db.SiteAdmins.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        return admin is null ? null : ToDto(admin);
    }

    public async Task<IReadOnlyList<SiteAdminDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.SiteAdmins.AsNoTracking().OrderBy(a => a.CreatedAt).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<SiteAdminDto> InviteAsync(Guid invitedByAdminId, string username, CancellationToken ct = default)
    {
        var normalized = username.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Username is required.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == normalized, ct)
            ?? throw new InvalidOperationException("User not found on this server.");

        if (await _db.SiteAdmins.AnyAsync(a => a.UserId == user.Id, ct))
            throw new InvalidOperationException("User is already an operator on this server.");

        var invite = new SiteAdmin
        {
            UserId = user.Id,
            Did = string.IsNullOrWhiteSpace(user.Did) ? null : user.Did,
            Username = user.Username,
            PasswordHash = null,
            InvitedByAdminId = invitedByAdminId
        };
        _db.SiteAdmins.Add(invite);
        await EnsureSuperUserAsync(user.Id, invitedByAdminId, ct);
        await _db.SaveChangesAsync(ct);
        return ToDto(invite);
    }

    public async Task RevokeAsync(Guid operatorId, CancellationToken ct = default)
    {
        var admin = await _db.SiteAdmins.FirstOrDefaultAsync(a => a.Id == operatorId, ct)
            ?? throw new InvalidOperationException("Operator not found.");

        if (admin.UserId != Guid.Empty && await _db.NodeConfig.AnyAsync(n => n.OwnerUserId == admin.UserId, ct))
            throw new InvalidOperationException("Cannot remove the server owner as an operator.");

        if (await _db.SiteAdmins.CountAsync(ct) <= 1)
            throw new InvalidOperationException("Cannot remove the last operator.");

        _db.SiteAdmins.Remove(admin);
        await _db.SaveChangesAsync(ct);
    }

    private async Task EnsureServerOwnerAsync(Guid userId, CancellationToken ct)
    {
        var node = await _db.NodeConfig.FirstOrDefaultAsync(ct);
        if (node is null || node.OwnerUserId.HasValue) return;
        node.OwnerUserId = userId;
    }

    private async Task EnsureSuperUserAsync(Guid userId, Guid grantedBySiteAdminId, CancellationToken ct)
    {
        if (!GrantOperatorsSuperUser)
            return;

        if (await _db.SuperUserGrants.AnyAsync(g => g.UserId == userId, ct))
            return;

        _db.SuperUserGrants.Add(new SuperUserGrant
        {
            UserId = userId,
            GrantedBySiteAdminId = grantedBySiteAdminId,
            Note = "Granted with operator access"
        });
    }

    private bool GrantOperatorsSuperUser =>
        bool.TryParse(_config["Admin:GrantOperatorsSuperUser"], out var enabled) && enabled;

    private static bool HasPassword(SiteAdmin admin) => !string.IsNullOrEmpty(admin.PasswordHash);

    private static string RequireNewPassword(OperatorLoginRequest request, string missingMessage)
    {
        var password = request.NewAdminPassword?.Trim();
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(missingMessage);
        if (password.Length < 8)
            throw new InvalidOperationException("Admin password must be at least 8 characters.");
        return password;
    }

    private string GenerateToken(SiteAdmin admin)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = DateTime.UtcNow.AddHours(int.Parse(_config["Admin:TokenExpiryHours"] ?? "8"));

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: SiteAdminClaims.Audience,
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
                new Claim(ClaimTypes.Name, admin.Username),
                new Claim("did", admin.Did ?? string.Empty),
                new Claim(SiteAdminClaims.Role, SiteAdminClaims.SiteAdmin)
            ],
            expires: expiry,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static SiteAdminDto ToDto(SiteAdmin admin) =>
        new(admin.Id, admin.UserId, admin.Username, admin.Did, HasPassword(admin), admin.CreatedAt);
}
