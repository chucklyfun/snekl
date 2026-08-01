using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Snekl.Domain.Entities;
using Snekl.Domain.Identity;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IExternalAuthService
{
    Task<User> GetOrCreateShadowUserAsync(ClaimsPrincipal principal, CancellationToken ct = default);
    Task<IReadOnlyList<SecurityKey>> ResolveSigningKeysAsync(string issuer, CancellationToken ct = default);
    bool IsTrustedIssuer(string issuer);
}

public class ExternalAuthService : IExternalAuthService
{
    private readonly SneklDbContext _db;
    private readonly IPersonClusterMappingService _mapping;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private static readonly ConcurrentDictionary<string, (DateTime Expires, IReadOnlyList<SecurityKey> Keys)> KeyCache = new();

    public ExternalAuthService(
        SneklDbContext db,
        IPersonClusterMappingService mapping,
        IHttpClientFactory httpFactory,
        IConfiguration config)
    {
        _db = db;
        _mapping = mapping;
        _httpFactory = httpFactory;
        _config = config;
    }

    public bool IsTrustedIssuer(string issuer)
    {
        var normalized = issuer.Trim().TrimEnd('/');
        if (normalized == (_config["Jwt:Issuer"] ?? "snekl")) return true;
        return _db.TrustedAuthIssuers.AsNoTracking().Any(i => i.IsActive && i.IssuerUrl == normalized);
    }

    public async Task<IReadOnlyList<SecurityKey>> ResolveSigningKeysAsync(string issuer, CancellationToken ct = default)
    {
        var normalized = issuer.Trim().TrimEnd('/');
        if (normalized == (_config["Jwt:Issuer"] ?? "snekl"))
        {
            var localKey = _config["Jwt:Key"] ?? "snekl-dev-secret-key-change-in-production-min-32-chars";
            return [new SymmetricSecurityKey(Encoding.UTF8.GetBytes(localKey))];
        }

        if (KeyCache.TryGetValue(normalized, out var cached) && cached.Expires > DateTime.UtcNow)
            return cached.Keys;

        var trusted = await _db.TrustedAuthIssuers.FirstOrDefaultAsync(i => i.IsActive && i.IssuerUrl == normalized, ct);
        if (trusted is null) return [];

        var http = _httpFactory.CreateClient();
        var jwks = await http.GetFromJsonAsync<JwksDocument>(trusted.JwksUrl, cancellationToken: ct);
        if (jwks?.Keys is null || jwks.Keys.Count == 0) return [];

        var keys = jwks.Keys
            .Where(k => k.Kty == "oct" && !string.IsNullOrWhiteSpace(k.N))
            .Select(k => (SecurityKey)new SymmetricSecurityKey(Convert.FromBase64String(k.N)))
            .ToList();

        KeyCache[normalized] = (DateTime.UtcNow.AddMinutes(10), keys);
        return keys;
    }

    public async Task<User> GetOrCreateShadowUserAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var did = principal.FindFirstValue("did");
        var issuer = principal.FindFirst("iss")?.Value
            ?? principal.FindFirstValue("issuer");
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        var username = principal.FindFirstValue("username") ?? principal.FindFirstValue(ClaimTypes.Name);
        var displayName = principal.FindFirstValue("display_name") ?? username ?? "User";
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email") ?? $"{username}@external.local";
        var clusterIdClaim = principal.FindFirstValue("cluster_id");

        if (string.IsNullOrWhiteSpace(did) || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(sub))
            throw new InvalidOperationException("External token missing required identity claims.");

        issuer = issuer.Trim().TrimEnd('/');
        var subjectId = Guid.Parse(sub);
        username = username!.Trim().ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.Did == did || (u.AuthIssuerUrl == issuer && u.AuthSubjectId == subjectId), ct);

        if (user is null)
        {
            var usernameTaken = await _db.Users.AnyAsync(u => u.Username == username, ct);
            if (usernameTaken)
                username = $"{username}-{did[^4..]}".ToLowerInvariant();

            user = new User
            {
                Username = username,
                DisplayName = displayName.Trim(),
                Email = email.Trim().ToLowerInvariant(),
                Bio = string.Empty,
                Did = did,
                AuthIssuerUrl = issuer,
                AuthSubjectId = subjectId,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"))
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            user.DisplayName = displayName.Trim();
            user.Did = did;
            user.AuthIssuerUrl = issuer;
            user.AuthSubjectId = subjectId;
            await _db.SaveChangesAsync(ct);
        }

        if (Guid.TryParse(clusterIdClaim, out var clusterId))
        {
            await _mapping.MapIdentityAsync(new MapIdentityRequest(
                clusterId,
                did,
                user.Username,
                user.DisplayName,
                issuer,
                null,
                issuer,
                subjectId), ct);
        }

        return user;
    }

    private sealed record JwksDocument([property: JsonPropertyName("keys")] IReadOnlyList<JwkDocumentKey> Keys);
    private sealed record JwkDocumentKey(string Kty, string? N);
}
