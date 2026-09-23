using System.Text;
using System.Text.RegularExpressions;

namespace Snekl.Domain.Emotes;

public static partial class EmoteSyntax
{
    public const int MinCodeLength = 2;
    public const int MaxCodeLength = 32;
    public const int MaxEmojiLength = 16;
    public const int MaxEmotesPerScope = 100;
    public const long MaxEmoteBytes = 256 * 1024;

    public static readonly DefaultHighlightedReaction[] DefaultHighlightedReactions =
    [
        new("👍", "thumbsup", "Thumbs up"),
        new("❤️", "heart", "Heart"),
        new("😂", "joy", "Joy"),
        new("😮", "open_mouth", "Surprised"),
        new("😢", "cry", "Cry"),
        new("🎉", "tada", "Party"),
    ];

    public readonly record struct DefaultHighlightedReaction(string Emoji, string Code, string Name);

    public static bool TryNormalizeCode(string? raw, out string code)
    {
        code = (raw ?? string.Empty).Trim().Trim(':').ToLowerInvariant();
        if (code.Length < MinCodeLength || code.Length > MaxCodeLength)
            return false;
        return CodeRegex().IsMatch(code);
    }

    public static bool IsValidUnicodeEmoji(string? raw)
    {
        var emoji = (raw ?? string.Empty).Trim();
        if (emoji.Length is 0 or > MaxEmojiLength)
            return false;

        var hasVisible = false;
        foreach (var rune in emoji.EnumerateRunes())
        {
            if (Rune.IsControl(rune) || Rune.IsWhiteSpace(rune) || rune.Value is ':' or '/' or '\\')
                return false;
            if (rune.Value is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_')
                return false;
            hasVisible = true;
        }

        return hasVisible;
    }

    public static string? NormalizeSkinEmoji(string? raw)
    {
        var emoji = (raw ?? string.Empty).Trim();
        if (emoji.Length == 0) return null;
        if (!IsValidUnicodeEmoji(emoji))
            throw new InvalidOperationException("Skin target must be a unicode emoji.");
        return emoji;
    }

    public static string EmojiMatchKey(string emoji) =>
        string.Concat(emoji.EnumerateRunes().Where(r => r.Value != 0xFE0F).Select(r => r.ToString()));

    public static string? NormalizeQualifier(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var builder = new StringBuilder();
        foreach (var ch in raw.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
                builder.Append(ch);
            else if (builder.Length > 0 && builder[^1] != '_')
                builder.Append('_');
        }

        var value = builder.ToString().Trim('_');
        if (value.Length == 0) return null;
        if (value.Length > MaxCodeLength)
            value = value[..MaxCodeLength].Trim('_');
        return value.Length == 0 ? null : value;
    }

    [GeneratedRegex("^[a-z0-9_]{2,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodeRegex();
}
