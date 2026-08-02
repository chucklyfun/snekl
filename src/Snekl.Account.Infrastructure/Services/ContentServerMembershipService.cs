using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;
using Snekl.Domain.Dtos;
using Snekl.Domain.Identity.Did;

namespace Snekl.Account.Infrastructure.Services;

public static class ContentServerStatusPresets
{
    public static IReadOnlyList<ContentServerStatusPresetInfo> All { get; } =
    [
        new("active", "Active", "Included in your mixed feed", true, false),
        new("favorite", "Favorite", "Pinned and sorted first in your feed", true, true),
        new("muted", "Muted", "Hidden from feed but still subscribed", false, false),
        new("blocked", "Blocked", "Hidden from feed; re-enable anytime", false, false)
    ];

    public static ContentServerStatusPresetInfo? Get(string preset) =>
        All.FirstOrDefault(p => p.Preset.Equals(preset, StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string? preset) =>
        Get(preset ?? "")?.Preset ?? "active";
}

public interface IContentServerMembershipService
{
    IReadOnlyList<ContentServerStatusPresetInfo> GetPresets();
    Task<IReadOnlyList<ContentServerMembershipDto>> ListAsync(Guid clusterId, CancellationToken ct = default);
    Task<ContentServerMembershipDto> SubscribeAsync(Guid clusterId, string baseUrl, CancellationToken ct = default);
    Task<ContentServerMembershipDto> UpdateStatusAsync(Guid clusterId, string serverDid, string status, CancellationToken ct = default);
    Task UnsubscribeAsync(Guid clusterId, string serverDid, CancellationToken ct = default);
}

public class ContentServerMembershipService : IContentServerMembershipService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly AccountDbContext _db;
    private readonly HttpClient _http;

    public ContentServerMembershipService(AccountDbContext db, HttpClient http)
    {
        _db = db;
        _http = http;
    }

    public IReadOnlyList<ContentServerStatusPresetInfo> GetPresets() => ContentServerStatusPresets.All;

    public async Task<IReadOnlyList<ContentServerMembershipDto>> ListAsync(Guid clusterId, CancellationToken ct = default)
    {
        var settings = await GetOrCreateSettingsAsync(clusterId, ct);
        return Deserialize(settings.ContentServerMembershipsJson);
    }

    public async Task<ContentServerMembershipDto> SubscribeAsync(Guid clusterId, string baseUrl, CancellationToken ct = default)
    {
        baseUrl = baseUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Base URL is required.");

        var remote = await FetchNodeInfoAsync(baseUrl, ct)
            ?? throw new InvalidOperationException("Unable to reach content server.");

        var serverDid = !string.IsNullOrWhiteSpace(remote.Did)
            ? remote.Did
            : DidWeb.FromBaseUrl(baseUrl);

        var settings = await GetOrCreateSettingsAsync(clusterId, ct);
        var memberships = Deserialize(settings.ContentServerMembershipsJson).ToList();

        if (memberships.Any(m => m.ServerDid.Equals(serverDid, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Already subscribed to this server.");

        var membership = new ContentServerMembershipDto(
            serverDid,
            baseUrl,
            remote.Name,
            remote.Id,
            "active",
            DateTime.UtcNow);

        memberships.Add(membership);
        await SaveAsync(settings, memberships, ct);
        return membership;
    }

    public async Task<ContentServerMembershipDto> UpdateStatusAsync(
        Guid clusterId,
        string serverDid,
        string status,
        CancellationToken ct = default)
    {
        var normalized = ContentServerStatusPresets.Normalize(status);
        var settings = await GetOrCreateSettingsAsync(clusterId, ct);
        var memberships = Deserialize(settings.ContentServerMembershipsJson).ToList();
        var index = memberships.FindIndex(m => m.ServerDid.Equals(serverDid, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            throw new InvalidOperationException("Subscription not found.");

        var existing = memberships[index];
        var updated = existing with { Status = normalized };
        memberships[index] = updated;
        await SaveAsync(settings, memberships, ct);
        return updated;
    }

    public async Task UnsubscribeAsync(Guid clusterId, string serverDid, CancellationToken ct = default)
    {
        var settings = await GetOrCreateSettingsAsync(clusterId, ct);
        var memberships = Deserialize(settings.ContentServerMembershipsJson).ToList();
        var removed = memberships.RemoveAll(m => m.ServerDid.Equals(serverDid, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            throw new InvalidOperationException("Subscription not found.");

        await SaveAsync(settings, memberships, ct);
    }

    private async Task<UserSettingsReplica> GetOrCreateSettingsAsync(Guid clusterId, CancellationToken ct)
    {
        var settings = await _db.SettingsReplicas.FirstOrDefaultAsync(s => s.PersonClusterId == clusterId, ct);
        if (settings is not null) return settings;

        settings = new UserSettingsReplica { PersonClusterId = clusterId };
        _db.SettingsReplicas.Add(settings);
        await _db.SaveChangesAsync(ct);
        return settings;
    }

    private async Task SaveAsync(
        UserSettingsReplica settings,
        IReadOnlyList<ContentServerMembershipDto> memberships,
        CancellationToken ct)
    {
        settings.ContentServerMembershipsJson = JsonSerializer.Serialize(memberships, JsonOptions);
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static List<ContentServerMembershipDto> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<ContentServerMembershipDto>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task<NodeInfoDto?> FetchNodeInfoAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            var url = $"{baseUrl.TrimEnd('/')}/api/node";
            return await _http.GetFromJsonAsync<NodeInfoDto>(url, JsonOptions, ct);
        }
        catch
        {
            return null;
        }
    }
}
