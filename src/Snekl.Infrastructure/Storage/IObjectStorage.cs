namespace Snekl.Infrastructure.Storage;

public interface IObjectStorage
{
    Task EnsureBucketAsync(CancellationToken ct = default);

    Task PutAsync(
        string key,
        Stream content,
        string contentType,
        long contentLength,
        CancellationToken ct = default);

    Task<(Stream Stream, string ContentType, long ContentLength)?> GetAsync(
        string key,
        CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}
