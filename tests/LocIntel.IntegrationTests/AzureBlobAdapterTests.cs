using Azure.Storage.Blobs;
using LocIntel.Integrations.AzureBlob;
using Microsoft.Extensions.Options;
using Testcontainers.Azurite;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Smokes the REAL Azure Blob adapter against Azurite: the same SAS-ticket
/// PUT/GET code path production uses against Azure Storage - no mocks. The
/// mirror image of the MinIO smoke for the S3 adapter (ADR 19).
/// </summary>
public sealed class AzuriteFixture : IAsyncLifetime
{
    private readonly AzuriteContainer _azurite = new AzuriteBuilder(
        "mcr.microsoft.com/azure-storage/azurite:latest"
    ).Build();

    public AzureBlobObjectStore Store { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _azurite.StartAsync();
        var cs = _azurite.GetConnectionString();
        await new BlobContainerClient(cs, "locintel-test").CreateIfNotExistsAsync();
        Store = new AzureBlobObjectStore(
            Options.Create(
                new AzureBlobOptions { ConnectionString = cs, ContainerName = "locintel-test" }
            )
        );
    }

    public async Task DisposeAsync() => await _azurite.DisposeAsync();
}

public class AzureBlobAdapterTests(AzuriteFixture fixture) : IClassFixture<AzuriteFixture>
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
    public async Task Sas_ticket_round_trip()
    {
        var key = "primary/test-org/files/azure-probe";
        var payload = "azure adapter proves the ticket contract"u8.ToArray();

        // SAS cannot constrain body size. Only the bounded API relay may upload.
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            fixture.Store.CreateUploadTicketAsync(key, "text/plain", payload.Length).AsTask()
        );
        await fixture.Store.WriteAsync(key, new MemoryStream(payload), "text/plain");
        using var http = new HttpClient();

        // server-side read (scan path)
        Assert.Equal(payload.LongLength, await fixture.Store.GetLengthAsync(key));
        await using (var stream = await fixture.Store.OpenReadAsync(key))
        using (var reader = new StreamReader(stream))
            Assert.Equal("azure adapter proves the ticket contract", await reader.ReadToEndAsync());

        // presigned download (client path)
        var url = await fixture.Store.GetDownloadUrlAsync(key, TimeSpan.FromMinutes(1));
        Assert.Equal("azure adapter proves the ticket contract", await http.GetStringAsync(url));

        // erasure path
        await fixture.Store.DeleteAsync(key);
        Assert.Null(await fixture.Store.GetLengthAsync(key));
    }
}
