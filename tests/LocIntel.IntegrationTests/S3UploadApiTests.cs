using System.Net;
using System.Net.Http.Json;
using Amazon.S3;
using LocIntel.Modules.Storage;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Minio;
using Wolverine;

namespace LocIntel.IntegrationTests;

public sealed class S3UploadApiFixture : ApiFixture
{
    private readonly MinioContainer minio = new MinioBuilder("minio/minio:latest").Build();

    public override async Task InitializeAsync()
    {
        await minio.StartAsync();
        using var admin = new AmazonS3Client(
            minio.GetAccessKey(),
            minio.GetSecretKey(),
            new AmazonS3Config
            {
                ServiceURL = minio.GetConnectionString(),
                ForcePathStyle = true,
                UseHttp = true,
            }
        );
        await admin.PutBucketAsync("upload-security");
        await base.InitializeAsync();
    }

    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Storage:Provider", "s3");
        builder.UseSetting("Storage:S3:ServiceUrl", minio.GetConnectionString());
        builder.UseSetting("Storage:S3:AccessKey", minio.GetAccessKey());
        builder.UseSetting("Storage:S3:SecretKey", minio.GetSecretKey());
        builder.UseSetting("Storage:S3:BucketName", "upload-security");
        builder.UseSetting("Storage:S3:ForcePathStyle", "true");
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await minio.DisposeAsync();
    }
}

public sealed class S3UploadApiTests(S3UploadApiFixture fixture) : IClassFixture<S3UploadApiFixture>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Deleting_a_live_ticket_hides_the_file_but_retains_bytes_and_budget_until_expiry(
        bool uploadBeforeDelete
    )
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var created = await Create(owner);
        using var storageClient = new HttpClient();
        if (uploadBeforeDelete)
            Assert.Equal(
                HttpStatusCode.OK,
                await Upload(storageClient, created.Ticket, "safe"u8.ToArray())
            );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await owner.DeleteAsync($"/api/files/{created.FileId}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.GetAsync($"/api/files/{created.FileId}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.GetAsync($"/api/files/{created.FileId}/download")).StatusCode
        );
        var visible = await owner.GetFromJsonAsync<FileListResponse>("/api/files");
        Assert.DoesNotContain(visible!.Items, file => file.Id == created.FileId);

        // A previously issued cloud ticket cannot be revoked by deleting its row.
        // If unused, it may still write once; keep that object charged and sweep it.
        if (!uploadBeforeDelete)
            Assert.Equal(
                HttpStatusCode.OK,
                await Upload(storageClient, created.Ticket, "safe"u8.ToArray())
            );
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            await Upload(storageClient, created.Ticket, "evil"u8.ToArray())
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.GetAsync($"/api/files/{created.FileId}")).StatusCode
        );

        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var db = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        var deleted = await db.Files.AsNoTracking().SingleAsync(f => f.Id == created.FileId);
        Assert.Equal(FileStatus.Quarantined, deleted.Status);
        Assert.NotNull(deleted.DeletedAt);
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();
        await using (var bytes = await store.OpenReadAsync(deleted.Key))
        using (var reader = new StreamReader(bytes))
            Assert.Equal("safe", await reader.ReadToEndAsync());

        var ids = new List<Guid> { created.FileId };
        for (var i = 0; i < 19; i++)
            ids.Add((await Create(owner)).FileId);
        using var overBudget = await owner.PostAsJsonAsync(
            "/api/files",
            new CreateFileRequest("over-budget", "text/plain", 4)
        );
        Assert.Equal(HttpStatusCode.TooManyRequests, overBudget.StatusCode);
        Assert.Equal(
            20,
            await db.Files.CountAsync(f =>
                f.Status == FileStatus.PendingUpload || f.Status == FileStatus.Quarantined
            )
        );

        await db
            .Files.Where(f => ids.Contains(f.Id))
            .ExecuteUpdateAsync(set =>
                set.SetProperty(f => f.CreatedAt, DateTimeOffset.UtcNow.AddHours(-2))
            );
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(
                new ExpirePendingUploads(),
                new DeliveryOptions { TenantId = fixture.OrgA.Value.ToString() }
            );
        Assert.Null(await store.GetLengthAsync(deleted.Key));
        Assert.Equal(
            20,
            await db.Files.CountAsync(f => ids.Contains(f.Id) && f.Status == FileStatus.Erased)
        );
    }

    private static async Task<CreateFileResponse> Create(HttpClient owner)
    {
        using var response = await owner.PostAsJsonAsync(
            "/api/files",
            new CreateFileRequest("s3-security-" + Guid.NewGuid().ToString("N"), "text/plain", 4)
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateFileResponse>())!;
    }

    private static async Task<HttpStatusCode> Upload(
        HttpClient client,
        UploadTicket ticket,
        byte[] bytes
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, ticket.Url)
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new("text/plain");
        foreach (var (name, value) in ticket.Headers)
            if (name is not ("Content-Type" or "Content-Length"))
                request.Headers.Add(name, value);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }
}
