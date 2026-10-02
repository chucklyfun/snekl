using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;
using Snekl.Account.Infrastructure.Security;
using Snekl.Domain;
using Snekl.Domain.Identity.Did;

namespace Snekl.Account.Infrastructure.Services;

public interface IOauthService
{
    Task EnsureDevAppsAsync(CancellationToken ct = default);
    Task<OAuthAppDto?> GetPublicAppAsync(string clientId, CancellationToken ct = default);
    Task<RegisterOAuthAppResponse> RegisterAppAsync(Guid? createdByClusterId, RegisterOAuthAppRequest request, CancellationToken ct = default);
    Task<AuthorizeInstallResponse> AuthorizeAsync(
        ClaimsPrincipal user,
        string accessToken,
        AuthorizeInstallRequest request,
        string? contentDeviceProof,
        CancellationToken ct = default);
    Task<OAuthTokenResponse> IssueTokenAsync(OAuthTokenRequest request, CancellationToken ct = default);
    Task<OAuthBotCreatedResponse> CreateBotAsync(ClaimsPrincipal user, CreateOAuthBotRequest request, CancellationToken ct = default);
    Task<OAuthBotCreatedResponse> ReissueBotAsync(ClaimsPrincipal user, string username, CancellationToken ct = default);
}

public class OauthService : IOauthService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly AccountDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IAuthServerService _server;
    private readonly IContentInstallLookup _installs;
    private readonly IConfiguration _config;

    public OauthService(
        AccountDbContext db,
        ITokenService tokens,
        IAuthServerService server,
        IContentInstallLookup installs,
        IConfiguration config)
    {
        _db = db;
        _tokens = tokens;
        _server = server;
        _installs = installs;
        _config = config;
    }

    public async Task EnsureDevAppsAsync(CancellationToken ct = default)
    {
        var clientId = _config["OAuth:Faker:ClientId"];
        var secret = _config["OAuth:Faker:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
            return;

        if (await _db.OAuthApps.AnyAsync(a => a.ClientId == clientId, ct))
            return;

        var redirect = _config["OAuth:Faker:RedirectUri"] ?? "http://localhost:5300/oauth/callback";
        _db.OAuthApps.Add(new OAuthApp
        {
            ClientId = clientId,
            ClientSecretHash = BCrypt.Net.BCrypt.HashPassword(secret),
            Name = _config["OAuth:Faker:Name"] ?? "Snekl Faker",
            RedirectUrisJson = JsonSerializer.Serialize(new[] { redirect }),
            AdminUrl = _config["OAuth:Faker:AdminUrl"] ?? "http://localhost:5300/",
            IframeUrl = null
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<OAuthAppDto?> GetPublicAppAsync(string clientId, CancellationToken ct = default)
    {
        var app = await _db.OAuthApps.AsNoTracking().FirstOrDefaultAsync(a => a.ClientId == clientId, ct);
        return app is null ? null : ToDto(app);
    }

    public async Task<RegisterOAuthAppResponse> RegisterAppAsync(
        Guid? createdByClusterId,
        RegisterOAuthAppRequest request,
        CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Name is required.");

        var uris = NormalizeRedirects(request.RedirectUris);
        if (uris.Count == 0)
            throw new InvalidOperationException("At least one redirect URI is required.");

        var clientId = string.IsNullOrWhiteSpace(request.ClientId)
            ? $"app_{Guid.NewGuid():N}"[..20]
            : request.ClientId.Trim();
        if (await _db.OAuthApps.AnyAsync(a => a.ClientId == clientId, ct))
            throw new InvalidOperationException("Client id is already registered.");

        var secret = SecureTokenHelper.GenerateToken();
        var app = new OAuthApp
        {
            ClientId = clientId,
            ClientSecretHash = BCrypt.Net.BCrypt.HashPassword(secret),
            Name = name,
            RedirectUrisJson = JsonSerializer.Serialize(uris),
            AdminUrl = EmptyToNull(request.AdminUrl),
            IframeUrl = EmptyToNull(request.IframeUrl),
            CreatedByPersonClusterId = createdByClusterId
        };
        _db.OAuthApps.Add(app);
        await _db.SaveChangesAsync(ct);
        return new RegisterOAuthAppResponse(app.ClientId, secret, app.Name, uris, app.AdminUrl, app.IframeUrl);
    }

    public async Task<AuthorizeInstallResponse> AuthorizeAsync(
        ClaimsPrincipal user,
        string accessToken,
        AuthorizeInstallRequest request,
        string? contentDeviceProof,
        CancellationToken ct = default)
    {
        if (!user.TryGetClusterId(out var clusterId))
            throw new UnauthorizedAccessException("Sign in to authorize this app.");

        var identityIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (!Guid.TryParse(identityIdValue, out var identityId))
            throw new UnauthorizedAccessException("Sign in to authorize this app.");

        if (!string.Equals(request.CodeChallengeMethod, "S256", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("code_challenge_method must be S256.");
        if (string.IsNullOrWhiteSpace(request.CodeChallenge) || request.CodeChallenge.Length < 43)
            throw new InvalidOperationException("Invalid code_challenge.");

        var app = await _db.OAuthApps.FirstOrDefaultAsync(a => a.ClientId == request.ClientId, ct)
            ?? throw new InvalidOperationException("Unknown application.");

        var redirect = request.RedirectUri.Trim();
        if (!ParseRedirects(app.RedirectUrisJson).Contains(redirect, StringComparer.Ordinal))
            throw new InvalidOperationException("redirect_uri is not registered for this application.");

        var contentBase = request.ContentBaseUrl.Trim().TrimEnd('/');
        var install = await _installs.GetAsync(contentBase, request.InstallId, accessToken, contentDeviceProof, ct)
            ?? throw new InvalidOperationException("Install was not found or you cannot authorize it.");

        if (!string.Equals(install.ClientId, app.ClientId, StringComparison.Ordinal))
            throw new InvalidOperationException("Install does not belong to this application.");

        var code = SecureTokenHelper.GenerateToken();
        _db.OAuthAuthorizationCodes.Add(new OAuthAuthorizationCode
        {
            CodeHash = SecureTokenHelper.HashToken(code),
            AppId = app.Id,
            PersonClusterId = clusterId,
            IdentityId = identityId,
            RedirectUri = redirect,
            CodeChallenge = request.CodeChallenge.Trim(),
            ContentBaseUrl = contentBase,
            InstallId = request.InstallId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        await _db.SaveChangesAsync(ct);

        var query = $"code={Uri.EscapeDataString(code)}";
        if (!string.IsNullOrWhiteSpace(request.State))
            query += $"&state={Uri.EscapeDataString(request.State)}";
        var joiner = redirect.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return new AuthorizeInstallResponse(redirect + joiner + query);
    }

    public async Task<OAuthTokenResponse> IssueTokenAsync(OAuthTokenRequest request, CancellationToken ct = default)
    {
        var grant = request.GrantType.Trim();
        if (grant.Equals("authorization_code", StringComparison.OrdinalIgnoreCase))
            return await ExchangeCodeAsync(request, ct);
        if (grant.Equals("refresh_token", StringComparison.OrdinalIgnoreCase))
            return await RefreshAsync(request, ct);
        throw new InvalidOperationException("Unsupported grant_type.");
    }

    public async Task<OAuthBotCreatedResponse> CreateBotAsync(
        ClaimsPrincipal user,
        CreateOAuthBotRequest request,
        CancellationToken ct = default)
    {
        var username = request.Username.Trim().ToLowerInvariant();
        if (username.Length < 2)
            throw new InvalidOperationException("Bot username is required.");
        if (await _db.PersonClusters.AnyAsync(c => c.CanonicalUsername == username, ct))
            throw new InvalidOperationException("Username already exists on this auth server.");

        var ctx = await RequireInstallAsync(user, ct);
        var config = await _server.GetOrCreateAsync(ct);
        var keys = DidKey.Generate();
        var cluster = new PersonCluster
        {
            Did = keys.Did,
            PublicKeyMultibase = DidKey.PublicKeyMultibase(keys.Did),
            CanonicalUsername = username,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? username : request.DisplayName.Trim(),
            Email = $"bot-{Guid.NewGuid():N}@bots.snekl.invalid",
            PrimaryAuthServerUrl = config.BaseUrl,
            IsBot = true,
            OAuthAppId = ctx.App.Id
        };
        var identity = new AuthIdentity
        {
            PersonCluster = cluster,
            AuthServerUrl = config.BaseUrl,
            IsPrimary = true
        };
        _db.PersonClusters.Add(cluster);
        _db.AuthIdentities.Add(identity);
        await _db.SaveChangesAsync(ct);
        return await IssueBotTokensAsync(ctx, identity, cluster, config.BaseUrl, ct);
    }

    public async Task<OAuthBotCreatedResponse> ReissueBotAsync(
        ClaimsPrincipal user,
        string username,
        CancellationToken ct = default)
    {
        var ctx = await RequireInstallAsync(user, ct);
        var name = username.Trim().ToLowerInvariant();
        if (name.Length < 2)
            throw new InvalidOperationException("Bot username is required.");

        var cluster = await _db.PersonClusters.FirstOrDefaultAsync(
            c => c.CanonicalUsername == name && c.IsBot && c.OAuthAppId == ctx.App.Id,
            ct) ?? throw new InvalidOperationException("Bot not found for this app.");
        var identity = await _db.AuthIdentities
            .FirstOrDefaultAsync(i => i.PersonClusterId == cluster.Id && i.IsPrimary, ct)
            ?? throw new InvalidOperationException("Bot identity not found.");
        var config = await _server.GetOrCreateAsync(ct);

        var previous = await _db.OAuthRefreshTokens
            .Where(t => t.PersonClusterId == cluster.Id && t.TokenUse == OAuthClaims.TokenUseBot && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var row in previous)
            row.RevokedAt = DateTime.UtcNow;

        return await IssueBotTokensAsync(ctx, identity, cluster, config.BaseUrl, ct);
    }

    private async Task<InstallContext> RequireInstallAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var tokenUse = user.FindFirstValue(OAuthClaims.TokenUse);
        if (!string.Equals(tokenUse, OAuthClaims.TokenUseInstall, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Create bots with an install token.");

        var clientId = user.FindFirstValue(OAuthClaims.Azp)
            ?? throw new UnauthorizedAccessException("Install token is missing azp.");
        if (!Guid.TryParse(user.FindFirstValue(OAuthClaims.InstallId), out var installId))
            throw new UnauthorizedAccessException("Install token is missing install_id.");

        var contentBase = user.FindFirstValue(OAuthClaims.ContentBaseUrl) ?? "";
        var app = await _db.OAuthApps.FirstOrDefaultAsync(a => a.ClientId == clientId, ct)
            ?? throw new InvalidOperationException("Application not found.");
        return new InstallContext(app, installId, contentBase);
    }

    private async Task<OAuthBotCreatedResponse> IssueBotTokensAsync(
        InstallContext ctx,
        AuthIdentity identity,
        PersonCluster cluster,
        string issuer,
        CancellationToken ct)
    {
        var lifetime = TimeSpan.FromHours(12);
        var access = _tokens.IssueToken(identity, cluster, issuer, new TokenIssueOptions(
            Lifetime: lifetime,
            Azp: ctx.App.ClientId,
            Bot: true,
            InstallId: ctx.InstallId,
            TokenUse: OAuthClaims.TokenUseBot,
            ContentBaseUrl: ctx.ContentBaseUrl));
        var refresh = await StoreRefreshAsync(ctx.App.Id, cluster.Id, identity.Id, OAuthClaims.TokenUseBot, ctx.ContentBaseUrl, ctx.InstallId, ct);
        await _db.SaveChangesAsync(ct);
        return new OAuthBotCreatedResponse(
            new OAuthBotDto(cluster.Id, cluster.Did, cluster.CanonicalUsername, cluster.DisplayName),
            access,
            refresh,
            DateTime.UtcNow.Add(lifetime));
    }

    private sealed record InstallContext(OAuthApp App, Guid InstallId, string ContentBaseUrl);

    private async Task<OAuthTokenResponse> ExchangeCodeAsync(OAuthTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.RedirectUri) ||
            string.IsNullOrWhiteSpace(request.ClientId) ||
            string.IsNullOrWhiteSpace(request.CodeVerifier))
            throw new InvalidOperationException("code, redirect_uri, client_id, and code_verifier are required.");
        if (!Pkce.IsValidVerifier(request.CodeVerifier))
            throw new InvalidOperationException("Invalid code_verifier.");

        var app = await RequireAppAsync(request.ClientId, request.ClientSecret, ct);
        var row = await FindCodeAsync(request.Code, ct);
        if (row.UsedAt is not null)
            throw new InvalidOperationException("Authorization code was already used.");
        if (row.ExpiresAt < DateTime.UtcNow)
            throw new InvalidOperationException("Authorization code expired.");
        if (row.AppId != app.Id)
            throw new InvalidOperationException("Authorization code does not belong to this client.");
        if (!string.Equals(row.RedirectUri, request.RedirectUri.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException("redirect_uri does not match the authorization request.");
        if (!Pkce.VerifyS256(request.CodeVerifier, row.CodeChallenge))
            throw new InvalidOperationException("Invalid code_verifier.");

        row.UsedAt = DateTime.UtcNow;
        var identity = await _db.AuthIdentities.Include(i => i.PersonCluster)
            .FirstAsync(i => i.Id == row.IdentityId, ct);
        var cluster = identity.PersonCluster
            ?? throw new InvalidOperationException("Person cluster not found.");
        var config = await _server.GetOrCreateAsync(ct);
        var lifetime = TimeSpan.FromHours(1);
        var access = _tokens.IssueToken(identity, cluster, config.BaseUrl, new TokenIssueOptions(
            Lifetime: lifetime,
            Azp: app.ClientId,
            InstallId: row.InstallId,
            TokenUse: OAuthClaims.TokenUseInstall,
            ContentBaseUrl: row.ContentBaseUrl));
        var refresh = await StoreRefreshAsync(
            app.Id, cluster.Id, identity.Id, OAuthClaims.TokenUseInstall, row.ContentBaseUrl, row.InstallId, ct);
        await _db.SaveChangesAsync(ct);
        return new OAuthTokenResponse(
            access,
            "Bearer",
            (int)lifetime.TotalSeconds,
            refresh,
            OAuthClaims.TokenUseInstall,
            row.InstallId,
            row.ContentBaseUrl,
            await _installs.GetRateLimitingAsync(row.ContentBaseUrl, ct));
    }

    private async Task<OAuthTokenResponse> RefreshAsync(OAuthTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || string.IsNullOrWhiteSpace(request.ClientId))
            throw new InvalidOperationException("refresh_token and client_id are required.");

        var app = await RequireAppAsync(request.ClientId, request.ClientSecret, ct);
        var hash = SecureTokenHelper.HashToken(request.RefreshToken);
        var row = await _db.OAuthRefreshTokens
            .Include(t => t.Identity)!.ThenInclude(i => i!.PersonCluster)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new InvalidOperationException("Invalid refresh token.");
        if (row.AppId != app.Id || row.RevokedAt is not null || row.ExpiresAt < DateTime.UtcNow)
            throw new InvalidOperationException("Invalid refresh token.");

        var identity = row.Identity ?? throw new InvalidOperationException("Identity not found.");
        var cluster = identity.PersonCluster ?? throw new InvalidOperationException("Person cluster not found.");
        var config = await _server.GetOrCreateAsync(ct);
        var bot = string.Equals(row.TokenUse, OAuthClaims.TokenUseBot, StringComparison.Ordinal);
        var lifetime = bot ? TimeSpan.FromHours(12) : TimeSpan.FromHours(1);
        var access = _tokens.IssueToken(identity, cluster, config.BaseUrl, new TokenIssueOptions(
            Lifetime: lifetime,
            Azp: app.ClientId,
            Bot: bot,
            InstallId: row.InstallId,
            TokenUse: row.TokenUse,
            ContentBaseUrl: row.ContentBaseUrl));
        row.RevokedAt = DateTime.UtcNow;
        var next = await StoreRefreshAsync(
            app.Id, cluster.Id, identity.Id, row.TokenUse, row.ContentBaseUrl, row.InstallId, ct);
        await _db.SaveChangesAsync(ct);
        return new OAuthTokenResponse(
            access,
            "Bearer",
            (int)lifetime.TotalSeconds,
            next,
            row.TokenUse,
            row.InstallId,
            row.ContentBaseUrl,
            await _installs.GetRateLimitingAsync(row.ContentBaseUrl, ct));
    }

    private async Task<OAuthAuthorizationCode> FindCodeAsync(string code, CancellationToken ct)
    {
        var hash = SecureTokenHelper.HashToken(code);
        return await _db.OAuthAuthorizationCodes.FirstOrDefaultAsync(c => c.CodeHash == hash, ct)
            ?? throw new InvalidOperationException("Invalid authorization code.");
    }

    private async Task<OAuthApp> RequireAppAsync(string clientId, string? clientSecret, CancellationToken ct)
    {
        var app = await _db.OAuthApps.FirstOrDefaultAsync(a => a.ClientId == clientId, ct)
            ?? throw new InvalidOperationException("Unknown application.");
        if (string.IsNullOrWhiteSpace(clientSecret) || !BCrypt.Net.BCrypt.Verify(clientSecret, app.ClientSecretHash))
            throw new UnauthorizedAccessException("Invalid client credentials.");
        return app;
    }

    private async Task<string> StoreRefreshAsync(
        Guid appId,
        Guid clusterId,
        Guid identityId,
        string tokenUse,
        string contentBaseUrl,
        Guid installId,
        CancellationToken ct)
    {
        var raw = SecureTokenHelper.GenerateToken();
        _db.OAuthRefreshTokens.Add(new OAuthRefreshToken
        {
            TokenHash = SecureTokenHelper.HashToken(raw),
            AppId = appId,
            PersonClusterId = clusterId,
            IdentityId = identityId,
            TokenUse = tokenUse,
            ContentBaseUrl = contentBaseUrl,
            InstallId = installId,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        await Task.CompletedTask;
        return raw;
    }

    private static OAuthAppDto ToDto(OAuthApp app) =>
        new(app.ClientId, app.Name, ParseRedirects(app.RedirectUrisJson), app.AdminUrl, app.IframeUrl);

    private static IReadOnlyList<string> ParseRedirects(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> NormalizeRedirects(IReadOnlyList<string>? uris) =>
        (uris ?? [])
        .Select(u => u.Trim())
        .Where(u => Uri.TryCreate(u, UriKind.Absolute, out _))
        .Distinct(StringComparer.Ordinal)
        .ToList();

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
