using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Snekl.Domain;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IChangeLogService
{
    Task RecordAsync(
        string scope,
        Guid? forumId,
        Guid? watercoolerId,
        string entityType,
        Guid entityId,
        string action,
        Guid? actorUserId,
        string summary,
        object? before,
        object? after,
        CancellationToken ct = default);

    Task<IReadOnlyList<ChangeLogEntryDto>> ListServerAsync(int take = 100, CancellationToken ct = default);
    Task<IReadOnlyList<ChangeLogEntryDto>> ListForumAsync(Guid forumId, int take = 100, CancellationToken ct = default);
    Task<IReadOnlyList<ChangeLogEntryDto>> ListWatercoolerAsync(Guid watercoolerId, int take = 100, CancellationToken ct = default);
}

public class ChangeLogService : IChangeLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly SneklDbContext _db;

    public ChangeLogService(SneklDbContext db) => _db = db;

    public async Task RecordAsync(
        string scope,
        Guid? forumId,
        Guid? watercoolerId,
        string entityType,
        Guid entityId,
        string action,
        Guid? actorUserId,
        string summary,
        object? before,
        object? after,
        CancellationToken ct = default)
    {
        _db.ChangeLogEntries.Add(new ChangeLogEntry
        {
            Scope = scope,
            ForumId = forumId,
            WatercoolerId = watercoolerId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            ActorUserId = actorUserId,
            Summary = summary,
            BeforeJson = Serialize(before),
            AfterJson = Serialize(after)
        });
        await _db.SaveChangesAsync(ct);
    }

    public Task<IReadOnlyList<ChangeLogEntryDto>> ListServerAsync(int take = 100, CancellationToken ct = default) =>
        QueryAsync(e => e.Scope == ChangeLogScopes.Server, take, ct);

    public Task<IReadOnlyList<ChangeLogEntryDto>> ListForumAsync(Guid forumId, int take = 100, CancellationToken ct = default) =>
        QueryAsync(e => e.Scope == ChangeLogScopes.Forum && e.ForumId == forumId, take, ct);

    public Task<IReadOnlyList<ChangeLogEntryDto>> ListWatercoolerAsync(
        Guid watercoolerId,
        int take = 100,
        CancellationToken ct = default) =>
        QueryAsync(e => e.Scope == ChangeLogScopes.Watercooler && e.WatercoolerId == watercoolerId, take, ct);

    private async Task<IReadOnlyList<ChangeLogEntryDto>> QueryAsync(
        System.Linq.Expressions.Expression<Func<ChangeLogEntry, bool>> predicate,
        int take,
        CancellationToken ct)
    {
        var limit = Math.Clamp(take, 1, 200);
        var rows = await _db.ChangeLogEntries
            .AsNoTracking()
            .Include(e => e.Actor)
            .Where(predicate)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return rows
            .Select(e => new ChangeLogEntryDto(
                e.Id,
                e.Scope,
                e.ForumId,
                e.WatercoolerId,
                e.EntityType,
                e.EntityId,
                e.Action,
                e.ActorUserId,
                e.Actor?.Username ?? "unknown",
                e.Summary,
                e.BeforeJson,
                e.AfterJson,
                e.CreatedAt))
            .ToList();
    }

    public static string? Serialize(object? value)
    {
        if (value is null) return null;
        return JsonSerializer.Serialize(value, JsonOptions);
    }
}
