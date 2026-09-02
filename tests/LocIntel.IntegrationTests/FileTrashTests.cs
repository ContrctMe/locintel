using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace LocIntel.IntegrationTests;

/// <summary>
/// ADR 25's tier-2 promise, kept (operability item 6): deletion is a trash
/// with a restore window; the sweep erases bytes only after it closes, and
/// a quarantined file can never launder itself Clean through the trash.
/// </summary>
public class FileTrashTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private async Task<(HttpClient Client, Guid FileId)> UploadCleanAsync(string name)
    {
        var client = await fixture.LoginAsync(ApiFixture.UserA);
        var bytes = System.Text.Encoding.UTF8.GetBytes("restorable content");
        var created = await client.PostAsJsonAsync(
            "/api/files",
            new
            {
                name,
                contentType = "text/plain",
                sizeBytes = bytes.Length,
            }
        );
        created.EnsureSuccessStatusCode();
        var issued = await created.Content.ReadFromJsonAsync<JsonElement>();
        var fileId = issued.GetProperty("fileId").GetGuid();
        var put = new HttpRequestMessage(
            HttpMethod.Put,
            issued.GetProperty("ticket").GetProperty("url").GetString()
        )
        {
            Content = new ByteArrayContent(bytes),
        };
        (await client.SendAsync(put)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/files/{fileId}/complete", null)).EnsureSuccessStatusCode();
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                var mine = (await ApiFixture.GetItemsAsync(client, "/api/files"))
                    .EnumerateArray()
                    .FirstOrDefault(f => f.GetProperty("id").GetGuid() == fileId);
                return mine.ValueKind != JsonValueKind.Undefined
                    && mine.GetProperty("status").GetString() == "Clean";
            },
            "the uploaded file to be scanned Clean"
        );
        return (client, fileId);
    }

    private static async Task<bool> ListedAsync(HttpClient client, Guid fileId, bool trash)
    {
        var files = await ApiFixture.GetItemsAsync(
            client,
            trash ? "/api/files?trash=true" : "/api/files"
        );
        return files.EnumerateArray().Any(f => f.GetProperty("id").GetGuid() == fileId);
    }

    [Fact]
    public async Task Delete_restores_and_the_window_sweep_erases()
    {
        var (client, fileId) = await UploadCleanAsync("comeback.txt");

        (await client.DeleteAsync($"/api/files/{fileId}")).EnsureSuccessStatusCode();
        Assert.False(await ListedAsync(client, fileId, trash: false));
        Assert.True(await ListedAsync(client, fileId, trash: true));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/files/{fileId}/download")).StatusCode
        );

        // restore: back in the library, downloadable again (bytes were kept)
        (await client.PostAsync($"/api/files/{fileId}/restore", null)).EnsureSuccessStatusCode();
        Assert.True(await ListedAsync(client, fileId, trash: false));
        (await client.GetAsync($"/api/files/{fileId}/download")).EnsureSuccessStatusCode();

        // delete again, backdate past the window, run the sweep: bytes go
        (await client.DeleteAsync($"/api/files/{fileId}")).EnsureSuccessStatusCode();
        await using (var conn = new NpgsqlConnection(fixture.PostgresConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE storage.files SET deleted_at = now() - interval '31 days' WHERE id = $1",
                conn
            );
            cmd.Parameters.AddWithValue(fileId);
            await cmd.ExecuteNonQueryAsync();
        }
        await fixture.PublishForOrgA(new LocIntel.Modules.Storage.PurgeFileTrash());
        var erased = false;
        for (var i = 0; i < 200 && !erased; i++)
        {
            erased = !await ListedAsync(client, fileId, trash: true);
            if (!erased)
                await Task.Delay(100);
        }
        Assert.True(erased, await fixture.DeadLetterSummary());
        // erased means erased: restore has nothing to bring back
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsync($"/api/files/{fileId}/restore", null)).StatusCode
        );
    }
}
