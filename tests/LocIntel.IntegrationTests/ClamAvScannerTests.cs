using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LocIntel.Integrations.ClamAV;
using LocIntel.Modules.Storage;
using LocIntel.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine;

namespace LocIntel.IntegrationTests;

/// <summary>Real clamd over TCP, real handler/DB, local disk object storage (not cloud storage).</summary>
public class ClamAvScannerTests(ClamAvFixture fixture) : IClassFixture<ClamAvFixture>
{
    [Fact]
    public async Task Clean_multiframe_upload_is_scanned_previewed_and_downloadable()
    {
        var content = new string('a', 300_000);
        var (client, id) = await Upload(content);
        using (client)
        {
            (await client.PostAsync($"/api/files/{id}/complete", null)).EnsureSuccessStatusCode();
            await WaitForStatus(client, id, "Clean", preview: true);
            await AssertDownload(client, id, content);
        }
    }

    [Fact]
    public async Task Infected_upload_is_quarantined_without_preview_or_download()
    {
        var (client, id) = await Upload(
            @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"
        );
        using (client)
        {
            (await client.PostAsync($"/api/files/{id}/complete", null)).EnsureSuccessStatusCode();
            await WaitForStatus(client, id, "Quarantined", preview: false);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync($"/api/files/{id}/download")).StatusCode
            );
        }
    }

    [Fact]
    public async Task Scanner_outage_preserves_unscanned_state_until_successful_retry()
    {
        var (client, id) = await Upload("retry only after a real clean verdict");
        using (client)
        {
            // Arrange the committed pre-scan state without racing background retries.
            // The other tests exercise HTTP complete -> durable delivery; here invoke
            // the real transactional pipeline explicitly to observe a failed attempt.
            await using var db = new NpgsqlConnection(fixture.PostgresConnectionString);
            await db.OpenAsync();
            await using var uploaded = new NpgsqlCommand(
                "UPDATE storage.files SET status = 'Uploaded' WHERE id = @id AND status = 'PendingUpload'",
                db
            );
            uploaded.Parameters.AddWithValue("id", id);
            Assert.Equal(1, await uploaded.ExecuteNonQueryAsync());
            using var scope = fixture.Factory.Services.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            await fixture.PauseScannerAsync();
            try
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    bus.InvokeForTenantAsync(
                        fixture.OrgA.Value.ToString(),
                        new ScanUploadedFile(id)
                    )
                );
                await WaitForStatus(client, id, "Uploaded", preview: false);
                await using var state = new NpgsqlCommand(
                    "SELECT scanned_at IS NULL AND preview_key IS NULL FROM storage.files WHERE id = @id",
                    db
                );
                state.Parameters.AddWithValue("id", id);
                Assert.Equal(true, await state.ExecuteScalarAsync());
                Assert.Equal(
                    HttpStatusCode.NotFound,
                    (await client.GetAsync($"/api/files/{id}/download")).StatusCode
                );
            }
            finally
            {
                await fixture.ResumeScannerAsync();
            }

            await bus.InvokeForTenantAsync(fixture.OrgA.Value.ToString(), new ScanUploadedFile(id));
            await WaitForStatus(client, id, "Clean", preview: true);
            await AssertDownload(client, id, "retry only after a real clean verdict");
        }
    }

    private async Task<(HttpClient Client, Guid Id)> Upload(string content)
    {
        Assert.IsType<ClamAvScanner>(fixture.Factory.Services.GetRequiredService<IVirusScanner>());
        var client = await fixture.LoginAsync(ApiFixture.UserA);
        var bytes = Encoding.UTF8.GetBytes(content);
        var response = await client.PostAsJsonAsync(
            "/api/files",
            new
            {
                name = $"clamd-{Guid.NewGuid():N}.txt",
                contentType = "text/plain",
                sizeBytes = bytes.Length,
            }
        );
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        using var put = new HttpRequestMessage(
            HttpMethod.Put,
            body.GetProperty("ticket").GetProperty("url").GetString()
        )
        {
            Content = new ByteArrayContent(bytes),
        };
        (await client.SendAsync(put)).EnsureSuccessStatusCode();
        return (client, body.GetProperty("fileId").GetGuid());
    }

    private static Task WaitForStatus(HttpClient client, Guid id, string status, bool preview) =>
        ApiFixture.WaitUntilAsync(
            async () =>
            {
                var file = (await ApiFixture.GetItemsAsync(client, "/api/files"))
                    .EnumerateArray()
                    .Single(f => f.GetProperty("id").GetGuid() == id);
                return file.GetProperty("status").GetString() == status
                    && file.GetProperty("hasPreview").GetBoolean() == preview;
            },
            $"file {id} to be {status} with preview={preview}"
        );

    private static async Task AssertDownload(HttpClient client, Guid id, string expected)
    {
        var ticket = await client.GetFromJsonAsync<JsonElement>($"/api/files/{id}/download");
        Assert.Equal(expected, await client.GetStringAsync(ticket.GetProperty("url").GetString()));
    }
}
