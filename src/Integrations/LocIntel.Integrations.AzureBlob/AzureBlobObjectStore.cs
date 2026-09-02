using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using LocIntel.Platform.Storage;

namespace LocIntel.Integrations.AzureBlob;

public sealed class AzureBlobOptions
{
    /// <summary>Account connection string - Azurite's well-known one for dev/test.</summary>
    public required string ConnectionString { get; set; }
    public required string ContainerName { get; set; }
}

/// <summary>
/// Azure Blob implementation of the object-storage port (ADR 19): the SAS is
/// the ticket - the browser PUTs and GETs blobs directly, bytes never proxy
/// through the API. Server-side reads/writes (scanning, derivatives, exports)
/// use the SDK. The same code path runs against Azurite in the adapter smoke.
/// </summary>
public sealed class AzureBlobObjectStore : IObjectStore
{
    private readonly BlobContainerClient _container;

    public AzureBlobObjectStore(IOptions<AzureBlobOptions> options)
    {
        _container = new BlobContainerClient(
            options.Value.ConnectionString,
            options.Value.ContainerName
        );
    }

    public async ValueTask<UploadTicket> CreateUploadTicketAsync(
        string key,
        string contentType,
        long maxBytes,
        CancellationToken ct = default
    )
    {
        var blob = _container.GetBlobClient(key);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        var sas = blob.GenerateSasUri(
            new BlobSasBuilder(BlobSasPermissions.Create | BlobSasPermissions.Write, expiresAt)
            {
                ContentType = contentType,
            }
        );
        // raw PUT to a block blob needs the blob-type header alongside the SAS
        return new UploadTicket(
            sas.ToString(),
            "PUT",
            new Dictionary<string, string>
            {
                ["x-ms-blob-type"] = "BlockBlob",
                ["Content-Type"] = contentType,
            },
            expiresAt
        );
    }

    public ValueTask<Uri> GetDownloadUrlAsync(
        string key,
        TimeSpan ttl,
        CancellationToken ct = default
    ) =>
        ValueTask.FromResult(
            _container
                .GetBlobClient(key)
                .GenerateSasUri(
                    new BlobSasBuilder(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(ttl))
                )
        );

    public async ValueTask<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        await _container.GetBlobClient(key).ExistsAsync(ct);

    public async ValueTask<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
        await _container.GetBlobClient(key).OpenReadAsync(cancellationToken: ct);

    public async ValueTask WriteAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken ct = default
    ) =>
        await _container
            .GetBlobClient(key)
            .UploadAsync(
                content,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                },
                ct
            );

    public async ValueTask DeleteAsync(string key, CancellationToken ct = default) =>
        await _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: ct);
}
