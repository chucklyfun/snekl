using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Snekl.Domain.Entities;
using Snekl.Domain.Identity;
using Snekl.Domain;
using Snekl.Infrastructure.Caching;
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
    private static readonly JsonSerializerOptions JwksJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SneklDbContext _db;
    private readonly IPersonClusterMappingService _mapping;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly IDistributedCache _cache;

    public ExternalAuthService(
        SneklDbContext db,
        IPersonClusterMappingService mapping,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        IDistributedCache cache)
    {
        _db = db;
        _mapping = mapping;
        _httpFactory = httpFactory;
        _config = config;
        _cache = cache;
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

        var cacheKey = CacheKeys.Jwks(normalized);
        var cachedKeys = await _cache.GetJsonAsync<List<string>>(cacheKey, ct);
        if (cachedKeys is { Count: > 0 })
        {
            return cachedKeys
                .Select(k => (SecurityKey)new SymmetricSecurityKey(Convert.FromBase64String(k)))
                .ToList();
        }

        var trusted = await _db.TrustedAuthIssuers.FirstOrDefaultAsync(i => i.IsActive && i.IssuerUrl == normalized, ct);
        if (trusted is null) return [];

        var http = _httpFactory.CreateClient();
        var jwks = await http.GetFromJsonAsync<JwksDocument>(trusted.JwksUrl, JwksJsonOptions, ct);
        if (jwks?.Keys is null || jwks.Keys.Count == 0) return [];

        var keyStrings = jwks.Keys
            .Where(k => k.Kty == "oct" && !string.IsNullOrWhiteSpace(k.N))
            .Select(k => k.N!)
            .ToList();

        if (keyStrings.Count > 0)
            await _cache.SetJsonAsync(cacheKey, keyStrings, TimeSpan.FromMinutes(10), ct);

        return keyStrings
            .Select(k => (SecurityKey)new SymmetricSecurityKey(Convert.FromBase64String(k)))
            .ToList();
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
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var cacheKey = CacheKeys.ShadowUser(issuer, subjectId);
        var cachedUserId = await _cache.GetStringAsync(cacheKey, ct);
        if (cachedUserId is not null && Guid.TryParse(cachedUserId, out var userId))
        {
            var cachedUser = await _db.Users.FindAsync([userId], ct);
            if (cachedUser is not null) return cachedUser;
        }

        var user = await FindShadowUserAsync(did, issuer, subjectId, normalizedEmail, ct);

        if (user is null)
        {
            var usernameTaken = await _db.Users.AnyAsync(u => u.Username == username, ct);
            if (usernameTaken)
                username = $"{username}-{did[^4..]}".ToLowerInvariant();

            user = new User
            {
                Username = username,
                DisplayName = displayName.Trim(),
                Email = normalizedEmail,
                Bio = string.Empty,
                Did = did,
                AuthIssuerUrl = issuer,
                AuthSubjectId = subjectId,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
                IsBot = principal.FindFirstValue(OAuthClaims.Bot) == "true",
                OAuthClientId = principal.FindFirstValue(OAuthClaims.Azp),
                ExtensionInstallId = Guid.TryParse(principal.FindFirstValue(OAuthClaims.InstallId), out var installId)
                    ? installId
                    : null
            };
            _db.Users.Add(user);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                user = await FindShadowUserAsync(did, issuer, subjectId, normalizedEmail, ct)
                    ?? throw new InvalidOperationException("Failed to provision shadow user.");
            }
        }
        else
        {
            user.DisplayName = displayName.Trim();
            user.Did = did;
            user.AuthIssuerUrl = issuer;
            user.AuthSubjectId = subjectId;
            user.Email = normalizedEmail;
            user.IsBot = principal.FindFirstValue(OAuthClaims.Bot) == "true" || user.IsBot;
            if (Guid.TryParse(principal.FindFirstValue(OAuthClaims.InstallId), out var existingInstallId))
                user.ExtensionInstallId = existingInstallId;
            if (!string.IsNullOrWhiteSpace(principal.FindFirstValue(OAuthClaims.Azp)))
                user.OAuthClientId = principal.FindFirstValue(OAuthClaims.Azp);

            if (!string.Equals(user.Username, username, StringComparison.Ordinal))
            {
                var usernameTaken = await _db.Users.AnyAsync(u => u.Username == username && u.Id != user.Id, ct);
                if (!usernameTaken)
                    user.Username = username;
            }

            await _db.SaveChangesAsync(ct);
        }

        if (Guid.TryParse(clusterIdClaim, out var clusterId))
        {
            try
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
            catch (DbUpdateException)
            {
                // Another request may have mapped the same identity concurrently.
            }
        }

        await _cache.SetStringAsync(cacheKey, user.Id.ToString(), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        }, ct);

        return user;
    }

    /// <summary>
    /// Match an existing content user for an external auth identity. Email is included so
    /// re-registration (new did:key / auth subject, same email) reuses the shadow user.
    /// </summary>
    private Task<User?> FindShadowUserAsync(
        string did,
        string issuer,
        Guid subjectId,
        string normalizedEmail,
        CancellationToken ct) =>
        _db.Users.FirstOrDefaultAsync(u =>
            u.Did == did ||
            (u.AuthIssuerUrl == issuer && u.AuthSubjectId == subjectId) ||
            (u.Email == normalizedEmail && (u.AuthIssuerUrl == issuer || u.AuthIssuerUrl == null)), ct);

    private sealed record JwksDocument([property: JsonPropertyName("keys")] IReadOnlyList<JwkDocumentKey> Keys);
    private sealed record JwkDocumentKey(
        [property: JsonPropertyName("kty")] string Kty,
        [property: JsonPropertyName("n")] string? N);
}
