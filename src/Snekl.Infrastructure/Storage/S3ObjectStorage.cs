using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Snekl.Infrastructure.Storage;

public sealed class S3ObjectStorage : IObjectStorage, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly ObjectStorageOptions _options;
    private readonly ILogger<S3ObjectStorage> _logger;
    private int _bucketReady;

    public S3ObjectStorage(IOptions<ObjectStorageOptions> options, ILogger<S3ObjectStorage> logger)
    {
        _options = options.Value;
        _logger = logger;

        var config = new AmazonS3Config
        {
            ServiceURL = _options.Endpoint.TrimEnd('/'),
            ForcePathStyle = _options.ForcePathStyle,
            AuthenticationRegion = "us-east-1"
        };

        _client = new AmazonS3Client(_options.AccessKey, _options.SecretKey, config);
    }

    public async Task EnsureBucketAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _bucketReady, 1, 1) == 1)
            return;

        try
        {
            await _client.PutBucketAsync(new PutBucketRequest { BucketName = _options.Bucket }, ct);
        }
        catch (AmazonS3Exception ex) when (
            ex.StatusCode == System.Net.HttpStatusCode.Conflict ||
            string.Equals(ex.ErrorCode, "BucketAlreadyOwnedByYou", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ex.ErrorCode, "BucketAlreadyExists", StringComparison.OrdinalIgnoreCase))
        {
            // Bucket already exists — fine.
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _bucketReady, 0);
            _logger.LogWarning(ex, "Unable to ensure object-storage bucket {Bucket}", _options.Bucket);
            throw;
        }

        Interlocked.Exchange(ref _bucketReady, 1);
    }

    public async Task PutAsync(
        string key,
        Stream content,
        string contentType,
        long contentLength,
        CancellationToken ct = default)
    {
        await EnsureBucketAsync(ct);

        var request = new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            AutoResetStreamPosition = false
        };

        if (contentLength >= 0)
            request.Headers.ContentLength = contentLength;

        await _client.PutObjectAsync(request, ct);
    }

    public async Task<(Stream Stream, string ContentType, long ContentLength)?> GetAsync(
        string key,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetObjectAsync(_options.Bucket, key, ct);
            return (response.ResponseStream, response.Headers.ContentType ?? "application/octet-stream", response.Headers.ContentLength);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await _client.DeleteObjectAsync(_options.Bucket, key, ct);
    }

    public void Dispose() => _client.Dispose();
}
