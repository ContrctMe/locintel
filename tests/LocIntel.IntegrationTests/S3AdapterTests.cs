using System.Text;
using Amazon.S3;
using LocIntel.Integrations.AmazonS3;
using Microsoft.Extensions.Options;
using Testcontainers.Minio;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Smokes the REAL S3 adapter against MinIO: the same presigned PUT/GET code
/// path production uses against AWS/R2 - no mocks.
/// </summary>
public sealed class MinioFixture : IAsyncLifetime
{
    private readonly MinioContainer _minio = new MinioBuilder("minio/minio:latest").Build();
    public S3ObjectStore Store { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _minio.StartAsync();
        var options = new S3Options
        {
            BucketName = "locintel-test",
            ServiceUrl = _minio.GetConnectionString(),
            AccessKey = _minio.GetAccessKey(),
            SecretKey = _minio.GetSecretKey(),
            ForcePathStyle = true,
        };
        using var admin = new AmazonS3Client(
            options.AccessKey,
            options.SecretKey,
            new AmazonS3Config
            {
                ServiceURL = options.ServiceUrl,
                ForcePathStyle = true,
                UseHttp = options.ServiceUrl!.StartsWith("http://"),
            }
        );
        try
        {
            await admin.PutBucketAsync(options.BucketName);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"minio at '{options.ServiceUrl}': {e.Message}", e);
        }
        Store = new S3ObjectStore(Options.Create(options));
    }

    public async Task DisposeAsync() => await _minio.DisposeAsync();
}

public class S3AdapterTests(MinioFixture fixture) : IClassFixture<MinioFixture>
{
    [Fact]
    public async Task Server_writes_preserve_bytes_and_distinguish_empty_from_missing()
    {
        const string key = "primary/test-org/files/server-write";
        Assert.Null(await fixture.Store.GetLengthAsync(key));
        await fixture.Store.WriteAsync(key, new MemoryStream(), "text/plain");
        Assert.Equal(0L, await fixture.Store.GetLengthAsync(key));
        await fixture.Store.WriteAsync(key, new MemoryStream("preview"u8.ToArray()), "text/plain");
        Assert.Equal(7L, await fixture.Store.GetLengthAsync(key));
        await using (var stream = await fixture.Store.OpenReadAsync(key))
        using (var reader = new StreamReader(stream))
            Assert.Equal("preview", await reader.ReadToEndAsync());
        await fixture.Store.DeleteAsync(key);
        Assert.Null(await fixture.Store.GetLengthAsync(key));
    }

    [Fact]
    public async Task Presigned_ticket_round_trip()
    {
        var key = "primary/test-org/files/probe";
        var payload = "s3 adapter proves the ticket contract"u8.ToArray();

        // ticket -> client-side PUT straight to storage
        var ticket = await fixture.Store.CreateUploadTicketAsync(key, "text/plain", payload.Length);
        using var http = new HttpClient();
        var put = new HttpRequestMessage(HttpMethod.Put, ticket.Url)
        {
            Content = new ByteArrayContent(payload),
        };
        put.Content.Headers.ContentType = new("text/plain");
        foreach (var (name, value) in ticket.Headers)
            if (name != "Content-Type")
                put.Headers.Add(name, value);
        var uploaded = await http.SendAsync(put);
        Assert.True(uploaded.IsSuccessStatusCode, uploaded.StatusCode.ToString());

        // server-side read (scan path)
        Assert.Equal(payload.LongLength, await fixture.Store.GetLengthAsync(key));
        await using (var stream = await fixture.Store.OpenReadAsync(key))
        using (var reader = new StreamReader(stream))
            Assert.Equal("s3 adapter proves the ticket contract", await reader.ReadToEndAsync());

        // presigned download (client path)
        var url = await fixture.Store.GetDownloadUrlAsync(key, TimeSpan.FromMinutes(1));
        Assert.Equal("s3 adapter proves the ticket contract", await http.GetStringAsync(url));

        // A retained upload ticket must not replace bytes after a clean scan.
        using var overwrite = new HttpRequestMessage(HttpMethod.Put, ticket.Url)
        {
            Content = new ByteArrayContent("replacement bytes"u8.ToArray()),
        };
        overwrite.Content.Headers.ContentType = new("text/plain");
        foreach (var (name, value) in ticket.Headers)
            if (name != "Content-Type")
                overwrite.Headers.Add(name, value);
        Assert.False((await http.SendAsync(overwrite)).IsSuccessStatusCode);
        Assert.Equal("s3 adapter proves the ticket contract", await http.GetStringAsync(url));

        // The condition must be signed, not merely an optional client header.
        using var omitCondition = new HttpRequestMessage(HttpMethod.Put, ticket.Url)
        {
            Content = new ByteArrayContent(payload),
        };
        omitCondition.Content.Headers.ContentType = new("text/plain");
        Assert.False((await http.SendAsync(omitCondition)).IsSuccessStatusCode);

        // erasure path
        await fixture.Store.DeleteAsync(key);
        Assert.Null(await fixture.Store.GetLengthAsync(key));
    }
}
