namespace Snekl.Domain;

public static class NsfwMedia
{
    public const int CoverMaxLength = 140;

    public static string NormalizeCover(string? cover)
    {
        var text = (cover ?? string.Empty).Trim();
        if (text.Length > CoverMaxLength)
            text = text[..CoverMaxLength].Trim();
        return text;
    }

    public static string RequireCover(string? cover)
    {
        var text = NormalizeCover(cover);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("A cover sentence is required for NSFW images.");
        return text;
    }
}
