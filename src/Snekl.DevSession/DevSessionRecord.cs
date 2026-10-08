namespace Snekl.DevSession;

public sealed class DevSessionFile
{
    public string RepoRoot { get; set; } = "";
    public List<DevSessionRecord> Sessions { get; set; } = [];
}

public sealed class DevSessionRecord
{
    public int Pid { get; set; }
    public string Service { get; set; } = "";
    public string? Urls { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? LastRequestAt { get; set; }
    public string? LastRequestMethod { get; set; }
    public string? LastRequestPath { get; set; }
    public string? LastRequestClient { get; set; }
    public DateTimeOffset? LastCursorRequestAt { get; set; }
    public long RequestCount { get; set; }
}
