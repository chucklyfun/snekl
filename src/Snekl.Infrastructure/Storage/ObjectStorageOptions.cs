namespace Snekl.Infrastructure.Storage;

public class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    public string Endpoint { get; set; } = "http://localhost:9000";
    public string AccessKey { get; set; } = "snekl";
    public string SecretKey { get; set; } = "sneklsecret";
    public string Bucket { get; set; } = "snekl-media";
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>Max upload size in bytes (default 10 MiB).</summary>
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;
}
