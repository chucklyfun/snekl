using System.Text.Json.Nodes;
using Snekl.Infrastructure.Search;

namespace Snekl.Infrastructure.Tests;

public class OpenSearchQueriesTests
{
    [Fact]
    public void SearchBody_BoostsTitle_FuzzyAndCollapse()
    {
        var json = OpenSearchQueries.SearchBody("dinner", ["aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"], 0, 20)
            .ToJsonString();
        Assert.Contains("title^3", json, StringComparison.Ordinal);
        Assert.Contains(OpenSearchQueries.Fuzziness, json, StringComparison.Ordinal);
        Assert.Contains("threadRootId", json, StringComparison.Ordinal);
        Assert.Contains("isDeleted", json, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexMapping_UsesForumTextAnalyzer()
    {
        var json = OpenSearchQueries.IndexMapping().ToJsonString();
        Assert.Contains("forum_text", json, StringComparison.Ordinal);
        Assert.Contains("threadRootId", json, StringComparison.Ordinal);
    }
}
