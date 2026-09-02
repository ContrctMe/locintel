using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The compliance export: all four audit kinds (ADR 12) as JSONL, assembled
/// by Storage, delivered to Files, served by the existing download flow.
/// </summary>
public class AuditExportTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Trail_lands_in_files_with_all_four_kinds()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);

        // leave a footprint in the trail, then export
        (
            await owner.PostAsJsonAsync("/contact-links", new { email = "trail@example.com" })
        ).EnsureSuccessStatusCode();
        // the footprint and the export both ride the outbox: wait for the
        // event row before exporting, or the archive can honestly miss it
        var recorded = false;
        for (var i = 0; i < 200 && !recorded; i++)
        {
            var trail = await owner.GetFromJsonAsync<JsonElement>("/api/audit/events");
            recorded = trail
                .EnumerateArray()
                .Any(e => e.GetProperty("eventName").GetString() == "contact.invited");
            if (!recorded)
                await Task.Delay(100);
        }
        Assert.True(recorded, await fixture.DeadLetterSummary());
        (await owner.PostAsync("/api/audit/export", null)).EnsureSuccessStatusCode();

        JsonElement export = default;
        var found = false;
        for (var i = 0; i < 200 && !found; i++)
        {
            var files = await ApiFixture.GetItemsAsync(owner, "/api/files");
            foreach (var file in files.EnumerateArray())
                if (
                    file.GetProperty("name").GetString()!.StartsWith("audit-export-")
                    && file.GetProperty("status").GetString() == "Clean"
                )
                {
                    export = file;
                    found = true;
                }
            if (!found)
                await Task.Delay(100);
        }
        Assert.True(found, await fixture.DeadLetterSummary());

        var download = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/files/{export.GetProperty("id").GetGuid()}/download"
        );
        var bytes = await owner.GetByteArrayAsync(download.GetProperty("url").GetString());
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Equal(
            ["access.jsonl", "authz.jsonl", "changes.jsonl", "events.jsonl", "manifest.json"],
            zip.Entries.Select(e => e.Name).Order().ToArray()
        );

        // the events slice really is JSONL of the org's trail
        using var events = new StreamReader(zip.GetEntry("events.jsonl")!.Open());
        var lines = (await events.ReadToEndAsync()).Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries
        );
        Assert.Contains(
            lines,
            l =>
                JsonDocument.Parse(l).RootElement.GetProperty("eventName").GetString()
                == "contact.invited"
        );
        using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
        Assert.All(
            manifest.RootElement.GetProperty("kinds").EnumerateArray(),
            k => Assert.False(k.GetProperty("truncated").GetBoolean())
        );
    }

    [Fact]
    public async Task Export_needs_the_audit_read_grant()
    {
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        var denied = await viewer.PostAsync("/api/audit/export", null);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, denied.StatusCode);
    }
}
