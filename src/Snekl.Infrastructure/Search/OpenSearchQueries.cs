using System.Text.Json.Nodes;

namespace Snekl.Infrastructure.Search;

public static class OpenSearchQueries
{
    /// <summary>
    /// Default AUTO is 1 edit at length 3–5, so "Live" matches "Like".
    /// Require 5+ characters before a single edit, 9+ before two.
    /// </summary>
    public const string Fuzziness = "AUTO:5,8";

    public static JsonObject SearchBody(string query, IReadOnlyList<string> forumIds, int from, int size)
    {
        var forumTerms = new JsonArray();
        foreach (var id in forumIds)
            forumTerms.Add(id);

        return new JsonObject
        {
            ["from"] = from,
            ["size"] = size,
            ["query"] = new JsonObject
            {
                ["function_score"] = new JsonObject
                {
                    ["query"] = new JsonObject
                    {
                        ["bool"] = new JsonObject
                        {
                            ["must"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["multi_match"] = new JsonObject
                                    {
                                        ["query"] = query,
                                        ["fields"] = new JsonArray { "title^3", "message", "authorUsername^1.5" },
                                        ["fuzziness"] = Fuzziness,
                                        ["operator"] = "and"
                                    }
                                }
                            },
                            ["filter"] = new JsonArray
                            {
                                new JsonObject { ["term"] = new JsonObject { ["isDeleted"] = false } },
                                new JsonObject { ["terms"] = new JsonObject { ["forumId"] = forumTerms } }
                            }
                        }
                    },
                    ["functions"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["gauss"] = new JsonObject
                            {
                                ["createdAt"] = new JsonObject
                                {
                                    ["origin"] = "now",
                                    ["scale"] = "14d",
                                    ["decay"] = 0.5
                                }
                            }
                        }
                    },
                    ["score_mode"] = "multiply",
                    ["boost_mode"] = "multiply"
                }
            },
            ["collapse"] = new JsonObject
            {
                ["field"] = "threadRootId",
                ["inner_hits"] = new JsonObject
                {
                    ["name"] = "best",
                    ["size"] = 1,
                    ["highlight"] = new JsonObject
                    {
                        ["fields"] = new JsonObject
                        {
                            ["title"] = new JsonObject { ["number_of_fragments"] = 0 },
                            ["message"] = new JsonObject
                            {
                                ["fragment_size"] = 160,
                                ["number_of_fragments"] = 1
                            }
                        }
                    }
                }
            }
        };
    }

    public static JsonObject IndexMapping() => new()
    {
        ["settings"] = new JsonObject
        {
            ["number_of_shards"] = 1,
            ["number_of_replicas"] = 0,
            ["analysis"] = new JsonObject
            {
                ["analyzer"] = new JsonObject
                {
                    ["forum_text"] = new JsonObject
                    {
                        ["type"] = "custom",
                        ["tokenizer"] = "standard",
                        ["filter"] = new JsonArray { "lowercase", "asciifolding", "english_possessive_stemmer", "english_stop", "english_stemmer" }
                    }
                },
                ["filter"] = new JsonObject
                {
                    ["english_stop"] = new JsonObject
                    {
                        ["type"] = "stop",
                        ["stopwords"] = "_english_"
                    },
                    ["english_stemmer"] = new JsonObject
                    {
                        ["type"] = "stemmer",
                        ["language"] = "english"
                    },
                    ["english_possessive_stemmer"] = new JsonObject
                    {
                        ["type"] = "stemmer",
                        ["language"] = "possessive_english"
                    }
                }
            }
        },
        ["mappings"] = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["postId"] = new JsonObject { ["type"] = "keyword" },
                ["threadRootId"] = new JsonObject { ["type"] = "keyword" },
                ["forumId"] = new JsonObject { ["type"] = "keyword" },
                ["parentPostId"] = new JsonObject { ["type"] = "keyword" },
                ["title"] = new JsonObject { ["type"] = "text", ["analyzer"] = "forum_text" },
                ["message"] = new JsonObject { ["type"] = "text", ["analyzer"] = "forum_text" },
                ["authorUsername"] = new JsonObject
                {
                    ["type"] = "text",
                    ["analyzer"] = "simple",
                    ["fields"] = new JsonObject
                    {
                        ["raw"] = new JsonObject { ["type"] = "keyword" }
                    }
                },
                ["createdAt"] = new JsonObject { ["type"] = "date" },
                ["isDeleted"] = new JsonObject { ["type"] = "boolean" }
            }
        }
    };
}
