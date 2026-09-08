using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Storage.Blobs;
using LocIntel.Modules.Storage;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Azurite;
using Wolverine;

namespace LocIntel.IntegrationTests;

public sealed class AzureUploadApiFixture : ApiFixture
{
    private readonly AzuriteContainer azure = new AzuriteBuilder(
        "mcr.microsoft.com/azure-storage/azurite:latest"
    ).Build();

    public override async Task InitializeAsync()
    {
        await azure.StartAsync();
        await new BlobContainerClient(
            azure.GetConnectionString(),
            "upload-security"
        ).CreateIfNotExistsAsync();
        await base.InitializeAsync();
    }

    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Storage:Provider", "azure");
        builder.UseSetting("Storage:Azure:ConnectionString", azure.GetConnectionString());
        builder.UseSetting("Storage:Azure:ContainerName", "upload-security");
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await azure.DisposeAsync();
    }
}

public sealed class AzureBoundedUploadTests(AzureUploadApiFixture fixture)
    : IClassFixture<AzureUploadApiFixture>
{
    [Fact]
    public async Task Azure_relay_authorizes_owner_bounds_unknown_length_and_cannot_replace_scanned_bytes()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var (id, url) = await Create(owner, 4);
        Assert.Equal($"/api/files/{id}/content", url);
        using var anonymous = fixture.Factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PutAsync(url, new ByteArrayContent("safe"u8.ToArray()))).StatusCode
        );
        using var otherOrg = await fixture.LoginAsync(ApiFixture.UserB);
        using var otherOwner = await fixture.LoginAsync(ApiFixture.UserBoth);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await otherOrg.PutAsync(url, new ByteArrayContent("safe"u8.ToArray()))).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await otherOwner.PutAsync(url, new ByteArrayContent("safe"u8.ToArray()))).StatusCode
        );
        var store = fixture.Factory.Services.GetRequiredService<IObjectStore>();
        var key = $"{RegionId.Default.Value}/{fixture.OrgA.Value}/files/{id}";
        using var oversized = new UnknownLengthContent("extra"u8.ToArray());
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            (await owner.PutAsync(url, oversized)).StatusCode
        );
        Assert.Null(await store.GetLengthAsync(key));
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await owner.PutAsync(url, new ByteArrayContent("safe"u8.ToArray()))).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await owner.PutAsync(url, new ByteArrayContent("evil"u8.ToArray()))).StatusCode
        );
        Assert.Equal(4L, await store.GetLengthAsync(key));
        (await owner.PostAsync($"/api/files/{id}/complete", null)).EnsureSuccessStatusCode();
        await ApiFixture.WaitUntilAsync(
            async () =>
                (await owner.GetFromJsonAsync<JsonElement>($"/api/files/{id}"))
                    .GetProperty("status")
                    .GetString() == "Clean",
            "the bounded Azure upload to scan clean"
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.PutAsync(url, new ByteArrayContent("evil"u8.ToArray()))).StatusCode
        );
        await using var actual = await store.OpenReadAsync(key);
        using var reader = new StreamReader(actual);
        Assert.Equal("safe", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Pending_admission_is_bounded_and_expired_uploads_are_removed_without_touching_held_bytes()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var (heldId, heldUrl) = await Create(owner, 1);
        (await owner.PutAsync(heldUrl, new ByteArrayContent([1]))).EnsureSuccessStatusCode();
        (
            await owner.PostAsJsonAsync($"/api/files/{heldId}/hold", new { hold = true })
        ).EnsureSuccessStatusCode();
        var pending = new List<Guid>();
        for (var i = 0; i < 19; i++)
            pending.Add((await Create(owner, 1)).Id);
        var over = await owner.PostAsJsonAsync(
            "/api/files",
            new
            {
                name = "over-budget",
                contentType = "text/plain",
                sizeBytes = 1,
            }
        );
        Assert.Equal(HttpStatusCode.TooManyRequests, over.StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var db = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        await db
            .Files.Where(f => f.Status == FileStatus.PendingUpload)
            .ExecuteUpdateAsync(set =>
                set.SetProperty(f => f.CreatedAt, DateTimeOffset.UtcNow.AddHours(-2))
            );
        var expiredUrl = $"/api/files/{pending[0]}/content";
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.PutAsync(expiredUrl, new ByteArrayContent([1]))).StatusCode
        );
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();
        var expiredKey = $"{RegionId.Default.Value}/{fixture.OrgA.Value}/files/{pending[0]}";
        await store.WriteAsync(expiredKey, new MemoryStream([1]), "text/plain");
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(
                new ExpirePendingUploads(),
                new DeliveryOptions { TenantId = fixture.OrgA.Value.ToString() }
            );
        Assert.Null(await store.GetLengthAsync(expiredKey));
        Assert.Equal(
            FileStatus.Erased,
            (await db.Files.AsNoTracking().SingleAsync(f => f.Id == pending[0])).Status
        );
        Assert.Equal(
            FileStatus.PendingUpload,
            (await db.Files.AsNoTracking().SingleAsync(f => f.Id == heldId)).Status
        );
        Assert.Equal(
            1L,
            await store.GetLengthAsync(
                $"{RegionId.Default.Value}/{fixture.OrgA.Value}/files/{heldId}"
            )
        );
        await Create(owner, 1); // Expiry released admission without erasing held bytes.
        (
            await owner.PostAsJsonAsync($"/api/files/{heldId}/hold", new { hold = false })
        ).EnsureSuccessStatusCode();
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(
                new ExpirePendingUploads(),
                new DeliveryOptions { TenantId = fixture.OrgA.Value.ToString() }
            );
        // Leave no pending state for other tests sharing this fixture.
        await db
            .Files.Where(f => f.Status == FileStatus.PendingUpload)
            .ExecuteUpdateAsync(set =>
                set.SetProperty(f => f.CreatedAt, DateTimeOffset.UtcNow.AddHours(-2))
            );
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(
                new ExpirePendingUploads(),
                new DeliveryOptions { TenantId = fixture.OrgA.Value.ToString() }
            );
    }

    [Fact]
    public async Task Declared_byte_budget_includes_quarantined_uploads_until_cleanup()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserB);
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
            ids.Add((await Create(owner, 100 * 1024 * 1024)).Id);
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgB, RegionId.Default);
        var db = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        await db
            .Files.Where(f => f.Id == ids[0])
            .ExecuteUpdateAsync(set => set.SetProperty(f => f.Status, FileStatus.Quarantined));
        var over = await owner.PostAsJsonAsync(
            "/api/files",
            new
            {
                name = "over-byte-budget",
                contentType = "text/plain",
                sizeBytes = 1,
            }
        );
        Assert.Equal(HttpStatusCode.TooManyRequests, over.StatusCode);
        await db
            .Files.Where(f => ids.Contains(f.Id))
            .ExecuteUpdateAsync(set =>
                set.SetProperty(f => f.CreatedAt, DateTimeOffset.UtcNow.AddHours(-2))
            );
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(
                new ExpirePendingUploads(),
                new DeliveryOptions { TenantId = fixture.OrgB.Value.ToString() }
            );
        Assert.Equal(
            5,
            await db.Files.CountAsync(f => ids.Contains(f.Id) && f.Status == FileStatus.Erased)
        );
        await Create(owner, 1);
    }

    private static async Task<(Guid Id, string Url)> Create(HttpClient client, int size)
    {
        var response = await client.PostAsJsonAsync(
            "/api/files",
            new
            {
                name = "azure-security-" + Guid.NewGuid().ToString("N"),
                contentType = "text/plain",
                sizeBytes = size,
            }
        );
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (
            body.GetProperty("fileId").GetGuid(),
            body.GetProperty("ticket").GetProperty("url").GetString()!
        );
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();
    }
}
