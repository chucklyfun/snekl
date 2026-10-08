namespace Snekl.Redis;

public class RateLimitOptions
{
    public const string SectionName = "RateLimiting";
    public bool Enabled { get; set; } = true;
    public List<RateLimitRule> Rules { get; set; } = [];
}

public class RateLimitRule
{
    public string PathPrefix { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public int Limit { get; set; } = 60;
    public int WindowSeconds { get; set; } = 60;
}
