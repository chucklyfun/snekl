using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Snekl.Domain;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResponse?> ReauthenticateAsync(Guid userId, string password, CancellationToken ct = default);
    Task<User?> GetUserByIdAsync(Guid id, CancellationToken ct = default);
}

public class AuthService : IAuthService
{
    private readonly SneklDbContext _db;
    private readonly IConfiguration _config;

    public AuthService(SneklDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var username = request.Username.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Username == username || u.Email == request.Email, ct))
            throw new InvalidOperationException("Username or email already exists.");

        var user = new User
        {
            Username = username,
            DisplayName = request.DisplayName.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return new AuthResponse(GenerateToken(user, DateTimeOffset.UtcNow), ToDto(user));
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var username = request.Username.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return null;

        return new AuthResponse(GenerateToken(user, DateTimeOffset.UtcNow), ToDto(user));
    }

    public async Task<AuthResponse?> ReauthenticateAsync(Guid userId, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(password))
            return null;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return null;

        return new AuthResponse(GenerateToken(user, DateTimeOffset.UtcNow), ToDto(user));
    }

    public Task<User?> GetUserByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    private string GenerateToken(User user, DateTimeOffset? authTime = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = DateTime.UtcNow.AddDays(int.Parse(_config["Jwt:ExpiryDays"] ?? "7"));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username)
        };
        if (authTime.HasValue)
            claims.Add(new Claim(OAuthClaims.AuthTime, authTime.Value.ToUnixTimeSeconds().ToString()));

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expiry,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static UserDto ToDto(User user) => new(
        user.Id,
        user.Username,
        user.DisplayName,
        user.Email,
        user.Bio,
        user.OriginNodeUrl,
        user.IsFederated);
}
