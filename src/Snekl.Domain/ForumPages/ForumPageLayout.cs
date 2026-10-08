using System.Text.Json;
using System.Text.Json.Serialization;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;

namespace Snekl.Domain.ForumPages;

public sealed class ForumPageRegion
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("typeKey")]
    public string TypeKey { get; set; } = "";

    [JsonPropertyName("settings")]
    public Dictionary<string, JsonElement>? Settings { get; set; }

    [JsonPropertyName("children")]
    public List<ForumPageRegion>? Children { get; set; }
}

public static class ForumPageLayout
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static ForumPageRegion CreateDefaultHomeSeed(Guid? pinnedWatercoolerId = null)
    {
        var children = new List<ForumPageRegion>
        {
            Leaf(ForumPageViewTypes.CoolerList),
        };

        if (pinnedWatercoolerId is Guid coolerId)
        {
            children.Add(Leaf(ForumPageViewTypes.WatercoolerView, new Dictionary<string, JsonElement>
            {
                ["watercoolerId"] = JsonSerializer.SerializeToElement(coolerId)
            }));
        }

        children.Add(Leaf(ForumPageViewTypes.SubforumGrid));
        children.Add(Leaf(ForumPageViewTypes.ForumView));

        return new ForumPageRegion
        {
            Id = Guid.NewGuid(),
            TypeKey = ForumPageViewTypes.Stack,
            Settings = new Dictionary<string, JsonElement>(),
            Children = children
        };
    }

    public static ForumPageRegion CreateDefaultExtraPageSeed() =>
        new()
        {
            Id = Guid.NewGuid(),
            TypeKey = ForumPageViewTypes.Stack,
            Settings = new Dictionary<string, JsonElement>(),
            Children = [Leaf(ForumPageViewTypes.ForumView)]
        };

    public static string Serialize(ForumPageRegion root) =>
        JsonSerializer.Serialize(root, JsonOptions);

    public static ForumPageRegion Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Layout JSON is required.");
        var root = JsonSerializer.Deserialize<ForumPageRegion>(json, JsonOptions)
            ?? throw new InvalidOperationException("Layout JSON is invalid.");
        Validate(root);
        return root;
    }

    public static ForumPageRegion ParseOrSeed(string? json, Guid? pinnedWatercoolerId = null)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            return CreateDefaultHomeSeed(pinnedWatercoolerId);
        try
        {
            return Parse(json);
        }
        catch
        {
            return CreateDefaultHomeSeed(pinnedWatercoolerId);
        }
    }

    public static ForumPageRegion ParseChrome(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Chrome layout JSON is required.");
        var root = JsonSerializer.Deserialize<ForumPageRegion>(json, JsonOptions)
            ?? throw new InvalidOperationException("Chrome layout JSON is invalid.");
        ValidateChrome(root);
        return root;
    }

    public static ForumPageRegion ParseChromeOrSeed(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            return CreateDefaultChromeSeed();
        try
        {
            return ParseChrome(json);
        }
        catch
        {
            return CreateDefaultChromeSeed();
        }
    }

    /// <summary>
    /// Removes top-level forum-header leaves from a page layout (used when migrating header into chrome).
    /// </summary>
    public static ForumPageRegion StripTopLevelForumHeaders(ForumPageRegion root)
    {
        if (root.Children is null || root.Children.Count == 0)
            return root;
        root.Children = root.Children
            .Where(c => !string.Equals(c.TypeKey, ForumPageViewTypes.ForumHeader, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (root.Children.Count == 0)
            root.Children.Add(Leaf(ForumPageViewTypes.ForumView));
        return root;
    }

    public static void Validate(ForumPageRegion root) => Validate(root, chrome: false);

    public static void ValidateChrome(ForumPageRegion root) => Validate(root, chrome: true);

    public static void Validate(ForumPageRegion root, bool chrome)
    {
        ValidateNode(root, depth: 0, isRoot: true, chrome);
        var bodyCount = CountType(root, ForumPageViewTypes.PageBody);
        if (chrome)
        {
            if (bodyCount != 1)
                throw new InvalidOperationException("Shared chrome must contain exactly one page-body slot.");
        }
        else if (bodyCount > 0)
        {
            throw new InvalidOperationException("page-body is only allowed in shared chrome, not on a page layout.");
        }
    }

    public static ForumPageRegion CreateDefaultChromeSeed() =>
        new()
        {
            Id = Guid.NewGuid(),
            TypeKey = ForumPageViewTypes.Stack,
            Settings = new Dictionary<string, JsonElement>
            {
                ["gap"] = JsonSerializer.SerializeToElement(16)
            },
            Children =
            [
                Leaf(ForumPageViewTypes.ForumHeader),
                Leaf(ForumPageViewTypes.PageBody)
            ]
        };

    /// <summary>
    /// Injects <paramref name="pageLayout"/> wherever chrome has a page-body leaf.
    /// Page-body size settings are copied onto the injected root.
    /// </summary>
    public static ForumPageRegion Compose(ForumPageRegion chrome, ForumPageRegion pageLayout)
    {
        var clone = CloneTree(chrome);
        return ReplacePageBody(clone, pageLayout) ?? CloneTree(pageLayout);
    }

    public static IReadOnlyList<string> SoftGuidance(ForumPageRegion root)
    {
        var warnings = SoftGuidanceCore(root);
        return warnings;
    }

    public static IReadOnlyList<string> SoftGuidanceChrome(ForumPageRegion root)
    {
        var warnings = new List<string>();
        var depth = MaxDepth(root);
        if (depth > 3)
            warnings.Add($"Layout depth is {depth}; consider keeping splits under 3.");

        var leaves = CountLeaves(root);
        if (leaves > 8)
            warnings.Add($"This chrome has {leaves} views; large shared layouts can be hard to scan.");

        if (CountType(root, ForumPageViewTypes.PageBody) != 1)
            warnings.Add("Shared chrome needs exactly one page-body slot for page content.");

        return warnings;
    }

    private static List<string> SoftGuidanceCore(ForumPageRegion root)
    {
        var warnings = new List<string>();
        var depth = MaxDepth(root);
        if (depth > 3)
            warnings.Add($"Layout depth is {depth}; consider keeping splits under 3.");

        var leaves = CountLeaves(root);
        if (leaves > 8)
            warnings.Add($"This page has {leaves} views; large layouts can be hard to scan.");

        var hasPrimary = HasType(root, ForumPageViewTypes.ForumView)
            || HasType(root, ForumPageViewTypes.WatercoolerView);
        if (!hasPrimary)
            warnings.Add("No forum-view or watercooler-view — the page may look empty of primary content.");

        return warnings;
    }

    /// <summary>
    /// Ensures a watercooler-view for <paramref name="watercoolerId"/> exists after cooler-list
    /// (or after header if no cooler-list). Removes other watercooler-view leaves when syncing a pin.
    /// Pass null to remove all watercooler-view leaves.
    /// </summary>
    public static ForumPageRegion SyncPinnedWatercooler(ForumPageRegion root, Guid? watercoolerId)
    {
        RemoveWatercoolerViews(root);
        if (watercoolerId is null)
            return root;

        if (!ForumPageViewTypes.Containers.Contains(root.TypeKey))
        {
            root = new ForumPageRegion
            {
                Id = Guid.NewGuid(),
                TypeKey = ForumPageViewTypes.Stack,
                Settings = new Dictionary<string, JsonElement>(),
                Children = [root]
            };
        }

        root.Children ??= [];
        var view = Leaf(ForumPageViewTypes.WatercoolerView, new Dictionary<string, JsonElement>
        {
            ["watercoolerId"] = JsonSerializer.SerializeToElement(watercoolerId.Value)
        });

        var coolerListIdx = root.Children.FindIndex(c =>
            string.Equals(c.TypeKey, ForumPageViewTypes.CoolerList, StringComparison.OrdinalIgnoreCase));
        if (coolerListIdx >= 0)
            root.Children.Insert(coolerListIdx + 1, view);
        else
        {
            var headerIdx = root.Children.FindIndex(c =>
                string.Equals(c.TypeKey, ForumPageViewTypes.ForumHeader, StringComparison.OrdinalIgnoreCase));
            if (headerIdx >= 0)
                root.Children.Insert(headerIdx + 1, view);
            else
                root.Children.Insert(0, view);
        }

        return root;
    }

    public static Guid? FindPinnedWatercoolerId(ForumPageRegion root)
    {
        if (string.Equals(root.TypeKey, ForumPageViewTypes.WatercoolerView, StringComparison.OrdinalIgnoreCase))
            return ReadWatercoolerId(root.Settings);

        if (root.Children is null) return null;
        foreach (var child in root.Children)
        {
            var found = FindPinnedWatercoolerId(child);
            if (found is not null) return found;
        }

        return null;
    }

    private static void ValidateNode(ForumPageRegion node, int depth, bool isRoot, bool chrome)
    {
        if (node.Id == Guid.Empty)
            node.Id = Guid.NewGuid();

        if (string.IsNullOrWhiteSpace(node.TypeKey))
            throw new InvalidOperationException("Each region needs a typeKey.");

        if (!ForumPageViewTypes.All.Contains(node.TypeKey))
            throw new InvalidOperationException($"Unknown region type '{node.TypeKey}'.");

        if (!chrome
            && string.Equals(node.TypeKey, ForumPageViewTypes.PageBody, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("page-body is only allowed in shared chrome.");

        var isContainer = ForumPageViewTypes.Containers.Contains(node.TypeKey);
        var children = node.Children ?? [];

        if (isContainer)
        {
            if (!isRoot && children.Count == 0)
                throw new InvalidOperationException($"Container '{node.TypeKey}' must have children.");
            ValidateContainerSettings(node);
            foreach (var child in children)
            {
                ValidateSizeSettings(child);
                ValidateNode(child, depth + 1, isRoot: false, chrome);
            }
        }
        else
        {
            if (children.Count > 0)
                throw new InvalidOperationException($"Leaf '{node.TypeKey}' cannot have children.");
            ValidateSizeSettings(node);
            if (string.Equals(node.TypeKey, ForumPageViewTypes.WatercoolerView, StringComparison.OrdinalIgnoreCase)
                && ReadWatercoolerId(node.Settings) is null)
                throw new InvalidOperationException("watercooler-view requires settings.watercoolerId.");
            if (string.Equals(node.TypeKey, ForumPageViewTypes.Extension, StringComparison.OrdinalIgnoreCase))
                ValidateExtension(node);
        }
    }

    private static ForumPageRegion CloneTree(ForumPageRegion node) =>
        new()
        {
            Id = node.Id,
            TypeKey = node.TypeKey,
            Settings = node.Settings is null
                ? null
                : new Dictionary<string, JsonElement>(node.Settings, StringComparer.OrdinalIgnoreCase),
            Children = node.Children?.Select(CloneTree).ToList()
        };

    private static ForumPageRegion? ReplacePageBody(ForumPageRegion node, ForumPageRegion pageLayout)
    {
        if (string.Equals(node.TypeKey, ForumPageViewTypes.PageBody, StringComparison.OrdinalIgnoreCase))
        {
            var injected = CloneTree(pageLayout);
            if (node.Settings is { Count: > 0 })
            {
                injected.Settings ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, value) in node.Settings)
                {
                    if (key is "sizeMode" or "size" or "SizeMode" or "Size")
                        injected.Settings[key] = value;
                }
            }
            return injected;
        }

        if (node.Children is null) return node;
        for (var i = 0; i < node.Children.Count; i++)
            node.Children[i] = ReplacePageBody(node.Children[i], pageLayout) ?? node.Children[i];
        return node;
    }

    private static int CountType(ForumPageRegion node, string typeKey)
    {
        var count = string.Equals(node.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (node.Children is null) return count;
        return count + node.Children.Sum(c => CountType(c, typeKey));
    }

    private static void ValidateContainerSettings(ForumPageRegion node)
    {
        if (node.Settings is null) return;

        if (TryReadNumber(node.Settings, "gap", out var gap) && gap < 0)
            throw new InvalidOperationException("settings.gap cannot be negative.");

        if (TryReadNumber(node.Settings, "stackBelow", out var stackBelow) && stackBelow < 0)
            throw new InvalidOperationException("settings.stackBelow cannot be negative.");

        if (TryReadNumber(node.Settings, "minHeight", out var minHeight) && minHeight <= 0)
            throw new InvalidOperationException("settings.minHeight must be positive when set.");

        if (string.Equals(node.TypeKey, ForumPageViewTypes.Grid, StringComparison.OrdinalIgnoreCase)
            && TryReadNumber(node.Settings, "columns", out var columns)
            && (columns < 1 || columns > 12 || columns != Math.Truncate(columns)))
            throw new InvalidOperationException("settings.columns must be an integer from 1 to 12.");
    }

    private static void ValidateSizeSettings(ForumPageRegion node)
    {
        if (node.Settings is null) return;

        string? sizeMode = null;
        if (node.Settings.TryGetValue("sizeMode", out var modeEl)
            || node.Settings.TryGetValue("SizeMode", out modeEl))
        {
            sizeMode = modeEl.ValueKind == JsonValueKind.String ? modeEl.GetString() : null;
            if (sizeMode is not null
                && !string.Equals(sizeMode, "fr", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sizeMode, "px", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("settings.sizeMode must be 'fr' or 'px'.");
        }

        if (TryReadNumber(node.Settings, "size", out var size))
        {
            if (size <= 0)
                throw new InvalidOperationException("settings.size must be positive.");
            var mode = sizeMode ?? "fr";
            if (string.Equals(mode, "fr", StringComparison.OrdinalIgnoreCase) && size <= 0)
                throw new InvalidOperationException("settings.size (fr) must be positive.");
        }

        if (TryReadNumber(node.Settings, "colSpan", out var colSpan)
            && (colSpan < 1 || colSpan != Math.Truncate(colSpan)))
            throw new InvalidOperationException("settings.colSpan must be a positive integer.");
    }

    private static bool TryReadNumber(Dictionary<string, JsonElement> settings, string key, out double value)
    {
        value = 0;
        if (!settings.TryGetValue(key, out var el)
            && !settings.TryGetValue(char.ToUpperInvariant(key[0]) + key[1..], out el))
            return false;
        if (el.ValueKind == JsonValueKind.Number)
        {
            value = el.GetDouble();
            return true;
        }
        if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), out var parsed))
        {
            value = parsed;
            return true;
        }
        throw new InvalidOperationException($"settings.{key} must be a number.");
    }

    public static ForumPageRegion FromDto(ForumPageRegionDto dto)
    {
        Dictionary<string, JsonElement>? settings = null;
        if (dto.Settings is { Count: > 0 })
            settings = dto.Settings.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        return new ForumPageRegion
        {
            Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id,
            TypeKey = dto.TypeKey,
            Settings = settings,
            Children = dto.Children?.Select(FromDto).ToList()
        };
    }

    private static readonly HashSet<string> ExtensionViews = new(StringComparer.OrdinalIgnoreCase)
    {
        "storefront", "studio", "desk", "requests"
    };

    private static void ValidateExtension(ForumPageRegion node)
    {
        var clientId = ReadSettingString(node.Settings, "clientId");
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("extension requires settings.clientId.");
        var view = ReadSettingString(node.Settings, "view");
        if (view is null || !ExtensionViews.Contains(view))
            throw new InvalidOperationException("extension requires settings.view (storefront, studio, desk, or requests).");
        if (string.Equals(view, "storefront", StringComparison.OrdinalIgnoreCase)
            && ReadWatercoolerId(node.Settings, "artistUserId") is null)
            throw new InvalidOperationException("A storefront requires settings.artistUserId.");
    }

    private static string? ReadSettingString(Dictionary<string, JsonElement>? settings, string key)
    {
        if (settings is null) return null;
        if (!settings.TryGetValue(key, out var el)
            && !settings.TryGetValue(char.ToUpperInvariant(key[0]) + key[1..], out el))
            return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    public static Guid? ReadWatercoolerId(Dictionary<string, JsonElement>? settings) =>
        ReadWatercoolerId(settings, "watercoolerId");

    public static Guid? ReadWatercoolerId(Dictionary<string, JsonElement>? settings, string key)
    {
        if (settings is null) return null;
        var pascal = char.ToUpperInvariant(key[0]) + key[1..];
        if (!settings.TryGetValue(key, out var el) && !settings.TryGetValue(pascal, out el))
            return null;
        if (el.ValueKind == JsonValueKind.String && Guid.TryParse(el.GetString(), out var fromString))
            return fromString;
        if (el.ValueKind == JsonValueKind.String) return null;
        try
        {
            return el.Deserialize<Guid?>();
        }
        catch
        {
            return null;
        }
    }

    private static void RemoveWatercoolerViews(ForumPageRegion node)
    {
        if (node.Children is null) return;
        node.Children.RemoveAll(c =>
            string.Equals(c.TypeKey, ForumPageViewTypes.WatercoolerView, StringComparison.OrdinalIgnoreCase));
        foreach (var child in node.Children)
            RemoveWatercoolerViews(child);
    }

    private static ForumPageRegion Leaf(string typeKey, Dictionary<string, JsonElement>? settings = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TypeKey = typeKey,
            Settings = settings ?? new Dictionary<string, JsonElement>(),
            Children = null
        };

    private static int MaxDepth(ForumPageRegion node) =>
        node.Children is { Count: > 0 }
            ? 1 + node.Children.Max(MaxDepth)
            : 1;

    private static int CountLeaves(ForumPageRegion node)
    {
        if (!ForumPageViewTypes.Containers.Contains(node.TypeKey))
            return 1;
        return node.Children?.Sum(CountLeaves) ?? 0;
    }

    private static bool HasType(ForumPageRegion node, string typeKey)
    {
        if (string.Equals(node.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase))
            return true;
        return node.Children?.Any(c => HasType(c, typeKey)) == true;
    }
}
