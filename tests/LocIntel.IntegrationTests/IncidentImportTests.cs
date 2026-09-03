using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>Historical incidents from CSV: stage with per-row validation and site resolution, then commit or discard.</summary>
public class IncidentImportTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Stage_validates_rows_and_commit_lands_the_valid_ones()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var hierarchy = await owner.GetAsync("/api/hierarchy");
        Guid rootId;
        if (hierarchy.StatusCode == HttpStatusCode.OK)
            rootId = (await hierarchy.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("nodes")
                .EnumerateArray()
                .First(n => n.GetProperty("depth").GetInt32() == 0)
                .GetProperty("id")
                .GetGuid();
        else
        {
            var created = await owner.PostAsJsonAsync(
                "/api/hierarchy",
                new { name = "Org A", levels = new[] { "Region" } }
            );
            created.EnsureSuccessStatusCode();
            rootId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("rootNodeId")
                .GetGuid();
        }
        var site = await owner.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Import Store",
                timeZone = "America/Denver",
            }
        );
        site.EnsureSuccessStatusCode();
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var csv = new StringBuilder()
            .AppendLine(
                "site,occurred_at,category,severity,title,narrative,loss_amount,police_report,tags"
            )
            .AppendLine(
                "Import Store,2026-01-15T03:30:00Z,Organized Retail Crime,High,\"Crew, three subjects\",\"Cleared the \"\"razor\"\" aisle\",812.40,DPD-26-0042,orc;razors"
            )
            .AppendLine("Nowhere Store,2026-01-16T10:00:00Z,Theft,Low,Unknown site,,,,")
            .AppendLine("import store,2026-01-17T10:00:00Z,Theft,Extreme,Bad severity,,,,")
            .ToString();
        var fileId = await UploadAsync(owner, "history.csv", Encoding.UTF8.GetBytes(csv));

        var staged = await owner.PostAsJsonAsync("/api/incidents/imports", new { fileId });
        Assert.True(staged.IsSuccessStatusCode, await staged.Content.ReadAsStringAsync());
        var batch = await staged.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = batch.GetProperty("id").GetGuid();
        Assert.Equal(3, batch.GetProperty("total").GetInt32());
        Assert.Equal(1, batch.GetProperty("valid").GetInt32());
        Assert.Equal(2, batch.GetProperty("invalid").GetInt32());

        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/incidents/imports/{batchId}");
        var rows = detail.GetProperty("rows").EnumerateArray().ToList();
        Assert.Empty(rows[0].GetProperty("errors").EnumerateArray());
        Assert.Contains(
            rows[1].GetProperty("errors").EnumerateArray(),
            e => e.GetString()!.Contains("unknown site")
        );
        Assert.Contains(
            rows[2].GetProperty("errors").EnumerateArray(),
            e => e.GetString() == "unknown severity"
        );

        // nothing landed yet
        var before = await owner.GetFromJsonAsync<JsonElement>($"/api/incidents?siteId={siteId}");
        Assert.Equal(0, before.GetProperty("total").GetInt32());
        var committed = await owner.PostAsync($"/api/incidents/imports/{batchId}/commit", null);
        committed.EnsureSuccessStatusCode();
        Assert.Equal(
            1,
            (await committed.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("created")
                .GetInt32()
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await owner.PostAsync($"/api/incidents/imports/{batchId}/commit", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await owner.PostAsync($"/api/incidents/imports/{batchId}/discard", null)).StatusCode
        );

        var after = await owner.GetFromJsonAsync<JsonElement>($"/api/incidents?siteId={siteId}");
        var landed = Assert.Single(after.GetProperty("items").EnumerateArray());
        // 03:30Z on the 15th is the evening of the 14th in Denver: the stamp follows the site
        Assert.Equal("2026-01-14", landed.GetProperty("businessDate").GetString());
        var incident = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/incidents/{landed.GetProperty("id").GetGuid()}"
        );
        Assert.Equal("Import", incident.GetProperty("source").GetString());
        Assert.Equal("Crew, three subjects", incident.GetProperty("title").GetString());
        Assert.Equal("Cleared the \"razor\" aisle", incident.GetProperty("narrative").GetString());
        Assert.Equal("DPD-26-0042", incident.GetProperty("policeReportNumber").GetString());
        Assert.Equal(
            new[] { "orc", "razors" },
            incident.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray()
        );

        // a second staging can be discarded; other tenants and tiers see nothing
        var again = await owner.PostAsJsonAsync("/api/incidents/imports", new { fileId });
        var againId = (await again.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        (
            await owner.PostAsync($"/api/incidents/imports/{againId}/discard", null)
        ).EnsureSuccessStatusCode();
        var list = await owner.GetFromJsonAsync<JsonElement>("/api/incidents/imports");
        Assert.Contains(
            list.GetProperty("items").EnumerateArray(),
            b =>
                b.GetProperty("id").GetGuid() == againId
                && b.GetProperty("status").GetString() == "Discarded"
        );
        var outsider = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.GetAsync($"/api/incidents/imports/{batchId}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.PostAsJsonAsync("/api/incidents/imports", new { fileId })).StatusCode
        );
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.GetAsync("/api/incidents/imports")).StatusCode
        );
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string name, byte[] bytes)
    {
        var created = await client.PostAsJsonAsync(
            "/api/files",
            new
            {
                name,
                contentType = "text/csv",
                sizeBytes = bytes.Length,
            }
        );
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var fileId = body.GetProperty("fileId").GetGuid();
        var put = new HttpRequestMessage(
            HttpMethod.Put,
            body.GetProperty("ticket").GetProperty("url").GetString()
        )
        {
            Content = new ByteArrayContent(bytes),
        };
        (await client.SendAsync(put)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/files/{fileId}/complete", null)).EnsureSuccessStatusCode();
        return fileId;
    }
}
