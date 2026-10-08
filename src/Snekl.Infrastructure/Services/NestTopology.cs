namespace Snekl.Infrastructure.Services;

public readonly record struct NestRef(Guid Id, Guid? ParentId, string Slug);

public static class NestTopology
{
    public const int MaxDepth = 64;

    public static Dictionary<Guid, Guid?> Parents(IEnumerable<NestRef> nodes) =>
        nodes.ToDictionary(n => n.Id, n => n.ParentId);

    public static Guid RootId(Guid id, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        var guard = 0;
        while (parents.TryGetValue(id, out var parent) && parent is Guid next)
        {
            if (++guard > MaxDepth)
                throw new InvalidOperationException("Nest hierarchy is too deep.");
            id = next;
        }

        return id;
    }

    public static List<Guid> BranchIds(Guid rootId, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        var children = new Dictionary<Guid, List<Guid>>();
        foreach (var (id, parent) in parents)
        {
            if (parent is not Guid parentId)
                continue;
            if (!children.TryGetValue(parentId, out var list))
                children[parentId] = list = [];
            list.Add(id);
        }

        var result = new List<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);
        var guard = 0;
        while (queue.Count > 0)
        {
            if (++guard > 10000)
                throw new InvalidOperationException("Nest hierarchy is too deep.");
            var id = queue.Dequeue();
            result.Add(id);
            if (!children.TryGetValue(id, out var kids))
                continue;
            foreach (var kid in kids)
                queue.Enqueue(kid);
        }

        return result;
    }

    public static (string? TopSlug, string Slug) PublicAddress(Guid id, IReadOnlyDictionary<Guid, NestRef> byId)
    {
        var parents = byId.ToDictionary(pair => pair.Key, pair => pair.Value.ParentId);
        var root = byId[RootId(id, parents)];
        var node = byId[id];
        return root.Id == id ? (null, node.Slug) : (root.Slug, node.Slug);
    }

    public static Guid? FindByPublicAddress(string slug, string? topSlug, IReadOnlyList<NestRef> nodes)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(topSlug))
        {
            return nodes
                .Where(n => n.ParentId is null && n.Slug == normalized)
                .Select(n => (Guid?)n.Id)
                .FirstOrDefault();
        }

        var top = topSlug.Trim().ToLowerInvariant();
        var root = nodes.FirstOrDefault(n => n.ParentId is null && n.Slug == top);
        if (root.Id == Guid.Empty || root.Slug != top)
            return null;

        var parents = Parents(nodes);
        foreach (var node in nodes)
        {
            if (node.Id == root.Id || node.Slug != normalized)
                continue;
            if (RootId(node.Id, parents) == root.Id)
                return node.Id;
        }

        return null;
    }

    public static string NextSlug(string slug, ISet<string> taken)
    {
        var baseSlug = string.IsNullOrWhiteSpace(slug) ? "nest" : slug.Trim().ToLowerInvariant();
        if (!taken.Contains(baseSlug))
            return baseSlug;

        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{baseSlug}-{n}";
            if (!taken.Contains(candidate))
                return candidate;
        }

        throw new InvalidOperationException("Could not rename the nest slug.");
    }

    public static HashSet<string> DescendantSlugs(
        Guid rootId,
        IReadOnlyDictionary<Guid, NestRef> byId,
        IReadOnlySet<Guid>? except)
    {
        var parents = byId.ToDictionary(pair => pair.Key, pair => pair.Value.ParentId);
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in BranchIds(rootId, parents))
        {
            if (id == rootId || (except is not null && except.Contains(id)))
                continue;
            slugs.Add(byId[id].Slug);
        }

        return slugs;
    }
}
