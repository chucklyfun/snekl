using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;

namespace Snekl.Account.Infrastructure.Services;

public interface IFederationMembershipService
{
    IReadOnlyList<ContentServerStatusPresetInfo> GetPresets();
    Task<IReadOnlyList<FederationMembershipDto>> ListAsync(Guid clusterId, CancellationToken ct = default);
    Task<FederationMembershipDto> SubscribeAsync(Guid clusterId, string baseUrl, CancellationToken ct = default);
    Task<FederationMembershipDto> UpdateStatusAsync(Guid clusterId, string baseUrl, string status, CancellationToken ct = default);
    Task UnsubscribeAsync(Guid clusterId, string baseUrl, CancellationToken ct = default);
}

public class FederationMembershipService : IFederationMembershipService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly AccountDbContext _db;
    private readonly HttpClient _http;

    public FederationMembershipService(AccountDbContext db, HttpClient http)
    {
        _db = db;
        _http = http;
    }

    public IReadOnlyList<ContentServerStatusPresetInfo> GetPresets() => ContentServerStatusPresets.All;

    public async Task<IReadOnlyList<FederationMembershipDto>> ListAsync(Guid clusterId, CancellationToken ct = default)
    {
        await EnsureClusterExistsAsync(clusterId, ct);
        var settings = await GetOrCreateSettingsAsync(clusterId, ct);
        return Deserialize(settings.FederationsJson);
    }

    public async Task<FederationMembershipDto> SubscribeAsync(Guid clusterId, string baseUrl, CancellationToken ct = default)
    {
        await EnsureClusterExistsAsync(clusterId, ct);
        baseUrl = ContentServerMembershipService.NormalizeContentServerUrl(baseUrl);
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Base URL is required.");

        var (config, fetchError) = await FetchConfigAsync(baseUrl, ct);
        if (config is null)
            throw new InvalidOperationException(fetchError ?? "Unable to reach federation config.");

        var settings = await GetOrCreateSettingsAsync(clusterId, ct);
        var memberships = Deserialize(settings.FederationsJson).ToList();
        if (memberships.Any(m => string.Equals(m.BaseUrl, config.BaseUrl, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Already subscribed to this federation.");

        var membership = new FederationMembershipDto(
            config.BaseUrl,
            config.Name,
            config.AuthIssuer,
            "active",
            DateTime.UtcNow);

        memberships.Add(membership);
        await SaveAsync(settings, memberships, ct);
        return membership;
    }

    public async Task<FederationMembershipDto> UpdateStatusAsync(
        Guid clusterId,
        string baseUrl,
        string status,
        CancellationToken ct = default)
    {
        await EnsureClusterExistsAsync(clusterId, ct);
        var normalized = ContentServerStatusPresets.Normalize(status);
        var settings = await GetOrCreateSettingsAsync(clusterId, ct);
        var memberships = Deserialize(settings.FederationsJson).ToList();
        var key = ContentServerMembershipService.NormalizeContentServerUrl(baseUrl);
        var index = memberships.FindIndex(m => string.Equals(m.BaseUrl, key, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            throw new InvalidOperationException("Federation not found.");

        var updated = memberships[index] with { Status = normalized };
        memberships[index] = updated;
        await SaveAsync(settings, memberships, ct);
        return updated;
    }

    public async Task UnsubscribeAsync(Guid clusterId, string baseUrl, CancellationToken ct = default)
    {
        await EnsureClusterExistsAsync(clusterId, ct);
        var settings = await GetOrCreateSettingsAsync(clusterId, ct);
        var memberships = Deserialize(settings.FederationsJson).ToList();
        var key = ContentServerMembershipService.NormalizeContentServerUrl(baseUrl);
        var removed = memberships.RemoveAll(m => string.Equals(m.BaseUrl, key, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            throw new InvalidOperationException("Federation not found.");

        await SaveAsync(settings, memberships, ct);
    }

    private async Task EnsureClusterExistsAsync(Guid clusterId, CancellationToken ct)
    {
        if (!await _db.PersonClusters.AnyAsync(c => c.Id == clusterId, ct))
            throw new UnauthorizedAccessException("Session expired. Please log in again.");
    }

    private async Task<UserSettingsReplica> GetOrCreateSettingsAsync(Guid clusterId, CancellationToken ct)
    {
        var settings = await _db.SettingsReplicas.FirstOrDefaultAsync(s => s.PersonClusterId == clusterId, ct);
        if (settings is not null) return settings;

        settings = new UserSettingsReplica { PersonClusterId = clusterId };
        _db.SettingsReplicas.Add(settings);
        try
        {
            await _db.SaveChangesAsync(ct);
            return settings;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            settings = await _db.SettingsReplicas.FirstOrDefaultAsync(s => s.PersonClusterId == clusterId, ct);
            if (settings is not null) return settings;
            throw new InvalidOperationException("Failed to load account settings.");
        }
    }

    private async Task SaveAsync(
        UserSettingsReplica settings,
        IReadOnlyList<FederationMembershipDto> memberships,
        CancellationToken ct)
    {
        settings.FederationsJson = JsonSerializer.Serialize(memberships, JsonOptions);
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static List<FederationMembershipDto> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<FederationMembershipDto>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task<(FetchedConfig? Config, string? Error)> FetchConfigAsync(string baseUrl, CancellationToken ct)
    {
        var paths = new[]
        {
            "/.well-known/snekl-federation.json",
            "/api/hub/config",
            "/api/hub"
        };

        string? lastError = null;
        foreach (var path in paths)
        {
            var url = $"{baseUrl.TrimEnd('/')}{path}";
            try
            {
                var doc = await _http.GetFromJsonAsync<FederationConfigWire>(url, JsonOptions, ct);
                if (doc is null)
                {
                    lastError = $"Federation at {baseUrl} returned an empty response.";
                    continue;
                }

                var name = string.IsNullOrWhiteSpace(doc.Name) ? baseUrl : doc.Name.Trim();
                var resolvedBase = string.IsNullOrWhiteSpace(doc.BaseUrl)
                    ? baseUrl
                    : doc.BaseUrl.Trim().TrimEnd('/');
                var issuer = doc.Auth?.Issuer ?? doc.AuthBaseUrl;
                return (new FetchedConfig(resolvedBase, name, issuer?.Trim().TrimEnd('/')), null);
            }
            catch (HttpRequestException ex)
            {
                lastError = $"Could not reach {url}: {ex.Message}";
            }
            catch (TaskCanceledException)
            {
                lastError = $"Timed out connecting to {url}.";
            }
            catch (Exception ex)
            {
                lastError = $"Failed to read federation config at {url}: {ex.Message}";
            }
        }

        return (null, lastError ?? $"Could not load a Snekl federation config from {baseUrl}.");
    }

    private sealed record FetchedConfig(string BaseUrl, string Name, string? AuthIssuer);

    private sealed class FederationConfigWire
    {
        public string? Schema { get; set; }
        public string? Name { get; set; }
        public string? BaseUrl { get; set; }
        public string? AuthBaseUrl { get; set; }
        public FederationAuthWire? Auth { get; set; }
    }

    private sealed class FederationAuthWire
    {
        public string? Issuer { get; set; }
        public string? Jwks { get; set; }
    }
}
