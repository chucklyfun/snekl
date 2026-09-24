using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IContentBlockCatalogService
{
    Task EnsureSeededAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ContentBlockTypeDto>> ListTypesAsync(CancellationToken ct = default);
    Task<HashSet<string>> NamesForScopeAsync(string scope, CancellationToken ct = default);
}

public class ContentBlockCatalogService : IContentBlockCatalogService
{
    private static readonly (string Key, string Name, string Description, int Position)[] Types =
    [
        ("calendar", "Calendar", "Stream times and other events.", 10),
        ("ads", "Advertising", "First-class ad placements.", 20),
        ("links", "Links", "Supplemental links and information.", 30),
        ("extension", "Extension", "Sandboxed iframe from an installed extension.", 40),
        ("cooler-list", "Watercooler list", "Directory of watercoolers under this forum.", 50),
        ("cooler-feed", "Watercooler feed", "Live watercooler with a full composer.", 60)
    ];

    private static readonly (string TypeKey, string Name, string Label, string Scope, int Position)[] Permissions =
    [
        ("calendar", "ManageCalendar", "Schedule and edit calendar events", ContentBlockScopes.Forum, 1),
        ("ads", "ManageAds", "Configure advertising in this forum", ContentBlockScopes.Forum, 1),
        ("ads", "ManageAds", "Configure advertising on this server", ContentBlockScopes.Server, 1),
        ("links", "ManageLinks", "Edit the links block", ContentBlockScopes.Forum, 1),
        ("cooler-list", "ManageCoolerList", "Place the watercooler directory block", ContentBlockScopes.Forum, 1),
        ("cooler-feed", "ManageCoolerFeed", "Place a watercooler feed block", ContentBlockScopes.Forum, 1)
    ];

    private readonly SneklDbContext _db;

    public ContentBlockCatalogService(SneklDbContext db) => _db = db;

    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        foreach (var (key, name, description, position) in Types)
        {
            var row = await _db.ContentBlockTypes.FirstOrDefaultAsync(t => t.Key == key, ct);
            if (row is null)
            {
                row = new ContentBlockType
                {
                    Key = key,
                    Name = name,
                    Description = description,
                    Position = position
                };
                _db.ContentBlockTypes.Add(row);
            }
            else
            {
                row.Name = name;
                row.Description = description;
                row.Position = position;
            }
        }

        await _db.SaveChangesAsync(ct);

        var types = await _db.ContentBlockTypes.ToDictionaryAsync(t => t.Key, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var (typeKey, name, label, scope, position) in Permissions)
        {
            if (!types.TryGetValue(typeKey, out var type)) continue;
            var row = await _db.ContentBlockPermissions.FirstOrDefaultAsync(
                p => p.BlockTypeId == type.Id && p.Name == name && p.Scope == scope, ct);
            if (row is null)
            {
                _db.ContentBlockPermissions.Add(new ContentBlockPermission
                {
                    BlockTypeId = type.Id,
                    Name = name,
                    Label = label,
                    Scope = scope,
                    Position = position
                });
            }
            else
            {
                row.Label = label;
                row.Position = position;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ContentBlockTypeDto>> ListTypesAsync(CancellationToken ct = default)
    {
        var types = await _db.ContentBlockTypes
            .AsNoTracking()
            .Include(t => t.Permissions)
            .OrderBy(t => t.Position)
            .ToListAsync(ct);

        return types.Select(t => new ContentBlockTypeDto(
            t.Key,
            t.Name,
            t.Description,
            t.Permissions
                .OrderBy(p => p.Position)
                .ThenBy(p => p.Name)
                .Select(p => new ContentBlockPermissionDto(p.Name, p.Label, p.Description, p.Scope))
                .ToList())).ToList();
    }

    public async Task<HashSet<string>> NamesForScopeAsync(string scope, CancellationToken ct = default)
    {
        var names = await _db.ContentBlockPermissions.AsNoTracking()
            .Where(p => p.Scope == scope)
            .Select(p => p.Name)
            .ToListAsync(ct);
        return names.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
