using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using LocIntel.Platform.Storage;

namespace LocIntel.Integrations.AmazonS3;

public sealed class S3Options
{
    public required string BucketName { get; set; }

    /// <summary>Point at MinIO/R2/etc.; null = real AWS.</summary>
    public string? ServiceUrl { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }

    /// <summary>Path-style is required by MinIO and most S3-compatibles.</summary>
    public bool ForcePathStyle { get; set; }
}

/// <summary>
/// S3-compatible IObjectStore (ADR 19): presigned PUT tickets and presigned
/// GET downloads - bytes never proxy through the API. Works against AWS S3,
/// MinIO, and R2 (the integration test smokes it against MinIO).
/// </summary>
public sealed class S3ObjectStore : IObjectStore
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly bool _plainHttp;

    public S3ObjectStore(IOptions<S3Options> options)
    {
        _bucket = options.Value.BucketName;
        var config = new AmazonS3Config { ForcePathStyle = options.Value.ForcePathStyle };
        if (options.Value.ServiceUrl is { } serviceUrl)
        {
            config.ServiceURL = serviceUrl;
            // presigned URLs inherit this; without it they come out https
            // against plain-http endpoints like local MinIO
            config.UseHttp = serviceUrl.StartsWith("http://");
            _plainHttp = config.UseHttp;
        }
        _client = options.Value is { AccessKey: { } accessKey, SecretKey: { } secretKey }
            ? new AmazonS3Client(accessKey, secretKey, config)
            : new AmazonS3Client(config);
    }

    public async ValueTask<UploadTicket> CreateUploadTicketAsync(
        string key,
        string contentType,
        long maxBytes,
        CancellationToken ct = default
    )
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(15);
        var url = await _client.GetPreSignedURLAsync(
            new GetPreSignedUrlRequest
            {
                BucketName = _bucket,
                Key = key,
                Verb = HttpVerb.PUT,
                Expires = expires.UtcDateTime,
                ContentType = contentType,
            }
        );
        return new UploadTicket(
            FixScheme(url),
            "PUT",
            new Dictionary<string, string> { ["Content-Type"] = contentType },
            expires
        );
    }

    public async ValueTask<Uri> GetDownloadUrlAsync(
        string key,
        TimeSpan ttl,
        CancellationToken ct = default
    ) =>
        new(
            FixScheme(
                await _client.GetPreSignedURLAsync(
                    new GetPreSignedUrlRequest
                    {
                        BucketName = _bucket,
                        Key = key,
                        Verb = HttpVerb.GET,
                        Expires = DateTime.UtcNow.Add(ttl),
                    }
                )
            )
        );

    /// <summary>SDK v4 presigns https regardless of UseHttp; match the configured endpoint's scheme.</summary>
    private string FixScheme(string url) =>
        _plainHttp && url.StartsWith("https://") ? "http://" + url["https://".Length..] : url;

    public async ValueTask<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(_bucket, key, ct);
            return true;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async ValueTask<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var response = await _client.GetObjectAsync(_bucket, key, ct);
        return response.ResponseStream;
    }

    public async ValueTask WriteAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken ct = default
    ) =>
        await _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = content,
                ContentType = contentType,
            },
            ct
        );

    public async ValueTask DeleteAsync(string key, CancellationToken ct = default) =>
        await _client.DeleteObjectAsync(_bucket, key, ct);
}
