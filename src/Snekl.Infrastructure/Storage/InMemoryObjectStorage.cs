using System.Collections.Concurrent;

namespace Snekl.Infrastructure.Storage;

/// <summary>Process-local object store for unit/integration tests.</summary>
public sealed class InMemoryObjectStorage : IObjectStorage
{
    private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> _objects = new();

    public Task EnsureBucketAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task PutAsync(
        string key,
        Stream content,
        string contentType,
        long contentLength,
        CancellationToken ct = default)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        _objects[key] = (buffer.ToArray(), contentType);
    }

    public Task<(Stream Stream, string ContentType, long ContentLength)?> GetAsync(
        string key,
        CancellationToken ct = default)
    {
        if (!_objects.TryGetValue(key, out var value))
            return Task.FromResult<(Stream, string, long)?>(null);

        Stream stream = new MemoryStream(value.Bytes, writable: false);
        return Task.FromResult<(Stream, string, long)?>((stream, value.ContentType, value.Bytes.Length));
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        _objects.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
