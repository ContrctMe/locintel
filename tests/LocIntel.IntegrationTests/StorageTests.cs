using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

/// <summary>ADR 19: tickets, quarantine, derivatives, hold, auditable erasure. ADR 29: idempotency.</summary>
public class StorageTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Eicar =
        @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    [Theory]
    [InlineData(0, HttpStatusCode.BadRequest)]
    [InlineData(5, HttpStatusCode.RequestEntityTooLarge)]
    public async Task Complete_validates_stored_bytes_not_the_declared_upload_size(
        int actualBytes,
        HttpStatusCode expected
    )
    {
        using var client = await fixture.LoginAsync(ApiFixture.UserA);
        var created = await client.PostAsJsonAsync(
            "/api/files",
            new
            {
                name = "size-probe.txt",
                contentType = "text/plain",
                sizeBytes = 4,
            }
        );
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("fileId").GetGuid();
        // Arrange what a cloud upload can store; do not rely on the local PUT
        // endpoint's Content-Length check to validate the completion boundary.
        var store = fixture.Factory.Services.GetRequiredService<IObjectStore>();
        await store.WriteAsync(
            $"{RegionId.Default.Value}/{fixture.OrgA.Value}/files/{id}",
            new MemoryStream(new byte[actualBytes]),
            "text/plain"
        );

        Assert.Equal(
            expected,
            (await client.PostAsync($"/api/files/{id}/complete", null)).StatusCode
        );
        var file = (await ApiFixture.GetItemsAsync(client, "/api/files"))
            .EnumerateArray()
            .Single(f => f.GetProperty("id").GetGuid() == id);
        Assert.Equal(
            actualBytes > 4 ? "Quarantined" : "PendingUpload",
            file.GetProperty("status").GetString()
        );
        Assert.False(file.GetProperty("hasPreview").GetBoolean());
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/files/{id}/download")).StatusCode
        );
    }

    private async Task<(HttpClient client, Guid fileId)> Upload(
        string name,
        string content,
        string contentType = "text/plain"
    )
    {
        var client = await fixture.LoginAsync(ApiFixture.UserA);
        var bytes = Encoding.UTF8.GetBytes(content);
        var created = await client.PostAsJsonAsync(
            "/api/files",
            new
            {
                name,
                contentType,
                sizeBytes = bytes.Length,
            }
        );
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var fileId = body.GetProperty("fileId").GetGuid();
        var ticket = body.GetProperty("ticket");

        // the ticket flow: client PUTs directly to storage, not through the API surface
        var put = new HttpRequestMessage(HttpMethod.Put, ticket.GetProperty("url").GetString())
        {
            Content = new ByteArrayContent(bytes),
        };
        (await client.SendAsync(put)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/files/{fileId}/complete", null)).EnsureSuccessStatusCode();
        return (client, fileId);
    }

    private async Task<string?> PollStatus(HttpClient client, Guid fileId, string until)
    {
        string? status = null;
        await ApiFixture.WaitUntilAsync(
            async () =>
                (
                    status = (await ApiFixture.GetItemsAsync(client, "/api/files"))
                        .EnumerateArray()
                        .FirstOrDefault(f => f.GetProperty("id").GetGuid() == fileId)
                        .TryGetProperty("status", out var s)
                        ? s.GetString()
                        : null
                ) == until,
            $"the file to reach status {until}"
        );
        return status;
    }

    [Fact]
    public async Task Clean_file_flows_ticket_scan_preview_download()
    {
        var (client, fileId) = await Upload("notes.txt", "hello locintel storage");
        Assert.Equal("Clean", await PollStatus(client, fileId, "Clean"));

        // derivative: text head-preview generated async
        await ApiFixture.WaitUntilAsync(
            async () =>
                (await ApiFixture.GetItemsAsync(client, "/api/files"))
                    .EnumerateArray()
                    .First(f => f.GetProperty("id").GetGuid() == fileId)
                    .GetProperty("hasPreview")
                    .GetBoolean(),
            "the file preview to be generated"
        );

        // download: authz at signing time, then the unguarded short-TTL URL
        var download = await client.GetFromJsonAsync<JsonElement>($"/api/files/{fileId}/download");
        var bytes = await client.GetStringAsync(download.GetProperty("url").GetString());
        Assert.Equal("hello locintel storage", bytes);
    }

    [Fact]
    public async Task File_status_is_addressable_beyond_the_first_page_and_tenant_isolated()
    {
        var (client, fileId) = await Upload("older-scan.txt", "clean content");
        using var owner = client;
        await PollStatus(owner, fileId, "Clean");
        // Populate completed files; outstanding upload tickets intentionally have a smaller budget.
        for (var i = 0; i < 50; i++)
        {
            var (newerClient, newerId) = await Upload($"newer-{i}.txt", "x");
            await PollStatus(newerClient, newerId, "Clean");
            newerClient.Dispose();
        }

        var page = await ApiFixture.GetItemsAsync(owner, "/api/files");
        Assert.DoesNotContain(
            page.EnumerateArray(),
            row => row.GetProperty("id").GetGuid() == fileId
        );
        using var status = await owner.GetAsync($"/api/files/{fileId}");
        status.EnsureSuccessStatusCode();
        Assert.Equal(
            "Clean",
            (await status.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("status")
                .GetString()
        );

        using var other = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await other.GetAsync($"/api/files/{fileId}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.GetAsync($"/api/files/{Guid.NewGuid()}")).StatusCode
        );
        using var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.GetAsync($"/api/files/{fileId}")).StatusCode
        );
        (await owner.DeleteAsync($"/api/files/{fileId}")).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.GetAsync($"/api/files/{fileId}")).StatusCode
        );
    }

    [Fact]
    public async Task Infected_file_is_quarantined_and_never_downloadable()
    {
        var (client, fileId) = await Upload("malware.txt", $"prefix {Eicar} suffix");
        Assert.Equal("Quarantined", await PollStatus(client, fileId, "Quarantined"));
        var download = await client.GetAsync($"/api/files/{fileId}/download");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode); // never confirm the bytes exist
    }

    [Fact]
    public async Task Legal_hold_blocks_deletion_and_deletion_is_audited()
    {
        var (client, fileId) = await Upload("evidence.txt", "hold me");
        await PollStatus(client, fileId, "Clean");

        (
            await client.PostAsJsonAsync($"/api/files/{fileId}/hold", new { hold = true })
        ).EnsureSuccessStatusCode();
        var blocked = await client.DeleteAsync($"/api/files/{fileId}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        (
            await client.PostAsJsonAsync($"/api/files/{fileId}/hold", new { hold = false })
        ).EnsureSuccessStatusCode();
        var deleted = await client.DeleteAsync($"/api/files/{fileId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // in the trash (tier 2): not downloadable, but restorable
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/files/{fileId}/download")).StatusCode
        );

        // the act is a domain event (ADR 19: AUDITABLE deletion)
        List<LocIntel.Modules.Audit.Data.DomainLogEntry> events = [];
        for (var i = 0; i < 50 && events.Count == 0; i++)
        {
            await Task.Delay(100);
            events = (
                await fixture.QueryAudit(db =>
                    Microsoft
                        .EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(
                            db.DomainEvents
                        )
                        .Where(a => a.EventName == "file.deleted")
                )
            )
                .Where(a => a.Payload.Contains("evidence.txt")) // jsonb: filter in memory
                .ToList();
        }
        Assert.NotEmpty(events);
    }

    [Fact]
    public async Task Idempotency_key_replays_and_conflicts()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserA);
        var key = Guid.NewGuid().ToString();

        HttpRequestMessage Request(string value) =>
            new(HttpMethod.Put, "/api/settings/idem.probe")
            {
                Content = JsonContent.Create(new { value }),
                Headers = { { "Idempotency-Key", key } },
            };

        var first = await client.SendAsync(Request("one"));
        first.EnsureSuccessStatusCode();
        var firstBody = await first.Content.ReadAsStringAsync();

        // same key + same request: REPLAYED, not re-executed
        var replay = await client.SendAsync(Request("one"));
        Assert.Equal(first.StatusCode, replay.StatusCode);
        Assert.Equal(firstBody, await replay.Content.ReadAsStringAsync());

        // same key + different body: refused
        var conflict = await client.SendAsync(Request("two"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, conflict.StatusCode);
    }

    [Fact]
    public async Task Files_are_tenant_isolated()
    {
        var (_, fileId) = await Upload("private-a.txt", "org A only");
        var clientB = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.GetAsync($"/api/files/{fileId}/download")).StatusCode
        );
        var list = await ApiFixture.GetItemsAsync(clientB, "/api/files");
        Assert.DoesNotContain(
            list.EnumerateArray(),
            f => f.GetProperty("name").GetString() == "private-a.txt"
        );
    }
}
