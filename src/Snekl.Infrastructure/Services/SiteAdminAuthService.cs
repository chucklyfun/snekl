using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BCrypt.Net;
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
    Task<SiteAdminAuthResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<SiteAdminDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task EnsureSeedAdminAsync(string username, string password, CancellationToken ct = default);
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

    public async Task<SiteAdminAuthResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var username = request.Username.Trim().ToLowerInvariant();
        var admin = await _db.SiteAdmins.FirstOrDefaultAsync(a => a.Username == username, ct);
        if (admin is null || !BCrypt.Net.BCrypt.Verify(request.Password, admin.PasswordHash))
            return null;

        return new SiteAdminAuthResponse(GenerateToken(admin), ToDto(admin));
    }

    public async Task<SiteAdminDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var admin = await _db.SiteAdmins.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        return admin is null ? null : ToDto(admin);
    }

    public async Task EnsureSeedAdminAsync(string username, string password, CancellationToken ct = default)
    {
        var normalized = username.Trim().ToLowerInvariant();
        if (await _db.SiteAdmins.AnyAsync(a => a.Username == normalized, ct))
            return;

        _db.SiteAdmins.Add(new SiteAdmin
        {
            Username = normalized,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
        });
        await _db.SaveChangesAsync(ct);
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
                new Claim(SiteAdminClaims.Role, SiteAdminClaims.SiteAdmin)
            ],
            expires: expiry,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static SiteAdminDto ToDto(SiteAdmin admin) =>
        new(admin.Id, admin.Username, admin.CreatedAt);
}
