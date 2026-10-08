namespace Snekl.Domain.Enums;

/// <summary>
/// Vote notices fire at a starting level, then at each order of magnitude after that (×10).
/// </summary>
public static class VoteAlertLevels
{
    public const int DefaultBase = 10;
    public const int MaxLevel = 1_000_000_000;

    public static int NormalizeBase(int value)
    {
        if (value < 1)
            return DefaultBase;
        return value > MaxLevel ? MaxLevel : value;
    }

    public static void RequireBase(int value)
    {
        if (value < 1 || value > MaxLevel)
            throw new InvalidOperationException($"Vote alerts start at a level from 1 to {MaxLevel}.");
    }

    /// <summary>
    /// The highest magnitude of <paramref name="alertBase"/> that this count has newly reached.
    /// </summary>
    public static int? Crossed(int previousCount, int count, int alertBase)
    {
        var floor = NormalizeBase(alertBase);
        if (count < floor || count <= previousCount)
            return null;

        long level = floor;
        int? crossed = null;
        while (level <= count && level <= MaxLevel)
        {
            if (level > previousCount)
                crossed = (int)level;
            if (level > MaxLevel / 10)
                break;
            level *= 10;
        }

        return crossed;
    }
}
