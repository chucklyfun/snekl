using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Snekl.Domain.Enums;

namespace Snekl.Infrastructure.Data;

public static class PermissionModesConverter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public static ValueConverter<Dictionary<string, GrantMode>, string> Converter { get; } = new(
        modes => Serialize(modes),
        json => Deserialize(json));

    public static ValueComparer<Dictionary<string, GrantMode>> Comparer { get; } = new(
        (a, b) => DictionaryEquals(a, b),
        m => DictionaryHash(m),
        m => new Dictionary<string, GrantMode>(m, StringComparer.OrdinalIgnoreCase));

    public static string Serialize(Dictionary<string, GrantMode> modes)
    {
        var payload = modes
            .Where(kv => kv.Value != GrantMode.None)
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static Dictionary<string, GrantMode> Deserialize(string? json)
    {
        var result = new Dictionary<string, GrantMode>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return result;

        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
        if (raw is null) return result;

        foreach (var (key, value) in raw)
        {
            if (Enum.TryParse<GrantMode>(value, true, out var mode) && mode != GrantMode.None)
                result[key] = mode;
        }

        return result;
    }

    private static bool DictionaryEquals(Dictionary<string, GrantMode>? a, Dictionary<string, GrantMode>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        foreach (var (key, mode) in a)
        {
            if (!b.TryGetValue(key, out var other) || other != mode)
                return false;
        }
        return true;
    }

    private static int DictionaryHash(Dictionary<string, GrantMode> modes)
    {
        var hash = new HashCode();
        foreach (var (key, mode) in modes.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            hash.Add(key, StringComparer.OrdinalIgnoreCase);
            hash.Add(mode);
        }
        return hash.ToHashCode();
    }
}
