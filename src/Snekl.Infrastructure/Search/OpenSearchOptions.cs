namespace Snekl.Infrastructure.Search;

public sealed class OpenSearchOptions
{
    public const string SectionName = "OpenSearch";

    public string Index { get; set; } = "snekl-posts";
}
