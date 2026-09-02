using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using LocIntel.Integrations.AzureBlob;
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
    public async Task Sas_ticket_round_trip()
    {
        var key = "primary/test-org/files/azure-probe";
        var payload = "azure adapter proves the ticket contract"u8.ToArray();

        // ticket -> client-side PUT straight to storage
        var ticket = await fixture.Store.CreateUploadTicketAsync(key, "text/plain", payload.Length);
        using var http = new HttpClient();
        var put = new HttpRequestMessage(HttpMethod.Put, ticket.Url)
        {
            Content = new ByteArrayContent(payload),
        };
        foreach (var (name, value) in ticket.Headers)
            if (name == "Content-Type")
                put.Content.Headers.ContentType = new(value);
            else
                put.Headers.Add(name, value);
        var uploaded = await http.SendAsync(put);
        Assert.True(uploaded.IsSuccessStatusCode, uploaded.StatusCode.ToString());

        // server-side read (scan path)
        Assert.True(await fixture.Store.ExistsAsync(key));
        await using (var stream = await fixture.Store.OpenReadAsync(key))
        using (var reader = new StreamReader(stream))
            Assert.Equal("azure adapter proves the ticket contract", await reader.ReadToEndAsync());

        // presigned download (client path)
        var url = await fixture.Store.GetDownloadUrlAsync(key, TimeSpan.FromMinutes(1));
        Assert.Equal("azure adapter proves the ticket contract", await http.GetStringAsync(url));

        // erasure path
        await fixture.Store.DeleteAsync(key);
        Assert.False(await fixture.Store.ExistsAsync(key));
    }
}
