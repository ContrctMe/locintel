using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The crime-intelligence fact table: stamps at write time (site-local
/// business date, ancestor path), the three gates, tier-2 deletion with
/// legal hold, and rollups over the stamps.
/// </summary>
public class IncidentTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Incident_round_trip_stamps_and_gates()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var siteId = await CreateSiteAsync(owner, "Auckland Store", "Pacific/Auckland");

        // 2026-03-01T13:00Z is already 2026-03-02 in Auckland (UTC+13 in March)
        var occurredAt = new DateTimeOffset(2026, 3, 1, 13, 0, 0, TimeSpan.Zero);
        var reported = await owner.PostAsJsonAsync(
            "/api/incidents",
            new
            {
                siteId,
                category = "OrganizedRetailCrime",
                severity = "High",
                title = "Crew cleared the razor aisle",
                occurredAt,
                narrative = "Three subjects, one distraction at the counter.",
                lossAmount = 1240.50,
                tags = new[] { "ORC", "razors", "orc" },
            }
        );
        Assert.True(reported.IsSuccessStatusCode, await reported.Content.ReadAsStringAsync());
        var created = await reported.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("2026-03-02", created.GetProperty("businessDate").GetString());

        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/incidents/{id}");
        Assert.Equal("Open", detail.GetProperty("status").GetString());
        Assert.Equal("Console", detail.GetProperty("source").GetString());
        Assert.Equal(ApiFixture.UserA, detail.GetProperty("reporter").GetString());
        Assert.StartsWith("n", detail.GetProperty("path").GetString()); // stamped ancestor path
        Assert.Equal(
            new[] { "orc", "razors" },
            detail.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray()
        );

        // list + filters run on the stamped business date
        var listed = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/incidents?siteId={siteId}&from=2026-03-02&to=2026-03-02&category=OrganizedRetailCrime"
        );
        Assert.Equal(1, listed.GetProperty("total").GetInt32());
        var missed = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/incidents?siteId={siteId}&from=2026-03-01&to=2026-03-01"
        );
        Assert.Equal(0, missed.GetProperty("total").GetInt32());

        // notes and attachments ride the incident
        var note = await owner.PostAsJsonAsync(
            $"/api/incidents/{id}/notes",
            new { body = "Matches the Wellington crew from last week." }
        );
        note.EnsureSuccessStatusCode();
        var fileId = await UploadAsync(owner, "cctv-still.jpg");
        var attached = await owner.PostAsJsonAsync(
            $"/api/incidents/{id}/attachments",
            new { fileId, label = "Register 3 camera" }
        );
        Assert.True(attached.IsSuccessStatusCode, await attached.Content.ReadAsStringAsync());
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await owner.PostAsJsonAsync($"/api/incidents/{id}/attachments", new { fileId })
            ).StatusCode
        );
        detail = await owner.GetFromJsonAsync<JsonElement>($"/api/incidents/{id}");
        Assert.Single(detail.GetProperty("notes").EnumerateArray());
        var attachment = Assert.Single(detail.GetProperty("attachments").EnumerateArray());
        Assert.Equal("cctv-still.jpg", attachment.GetProperty("fileName").GetString());

        // edit moves the instant; the stamp follows it in the SITE's zone
        var update = await owner.PutAsJsonAsync(
            $"/api/incidents/{id}",
            new
            {
                category = "Theft",
                severity = "Medium",
                title = "Razor aisle theft",
                occurredAt = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero),
                narrative = "Downgraded after review.",
                lossAmount = 300,
            }
        );
        Assert.True(update.IsSuccessStatusCode, await update.Content.ReadAsStringAsync());
        detail = await owner.GetFromJsonAsync<JsonElement>($"/api/incidents/{id}");
        Assert.Equal("2026-03-01", detail.GetProperty("businessDate").GetString());
        Assert.Equal("Theft", detail.GetProperty("category").GetString());

        // close with a reason, reopen
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await owner.PostAsJsonAsync($"/api/incidents/{id}/close", new { reason = " " })
            ).StatusCode
        );
        (
            await owner.PostAsJsonAsync($"/api/incidents/{id}/close", new { reason = "Unfounded" })
        ).EnsureSuccessStatusCode();
        detail = await owner.GetFromJsonAsync<JsonElement>($"/api/incidents/{id}");
        Assert.Equal("Closed", detail.GetProperty("status").GetString());
        Assert.Equal("Unfounded", detail.GetProperty("closureReason").GetString());
        (await owner.PostAsync($"/api/incidents/{id}/reopen", null)).EnsureSuccessStatusCode();

        // gates: another org's member sees nothing (404, never 403); guests
        // and role-less members hold nothing (401)
        var outsider = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.GetAsync($"/api/incidents/{id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await outsider.PostAsJsonAsync($"/api/incidents/{id}/notes", new { body = "x" })
            ).StatusCode
        );
        var outsiderList = await outsider.GetFromJsonAsync<JsonElement>("/api/incidents");
        Assert.DoesNotContain(
            outsiderList.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == id
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await outsider.PostAsJsonAsync(
                    "/api/incidents",
                    new
                    {
                        siteId,
                        category = "Theft",
                        severity = "Low",
                        title = "Not my site",
                        occurredAt,
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fixture.GuestClient().GetAsync("/api/incidents")).StatusCode
        );
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.GetAsync($"/api/incidents/{id}")).StatusCode
        );

        // legal hold blocks deletion; clearing it lets tier 2 proceed, restorable
        (
            await owner.PostAsJsonAsync($"/api/incidents/{id}/hold", new { hold = true })
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await owner.DeleteAsync($"/api/incidents/{id}")).StatusCode
        );
        (
            await owner.PostAsJsonAsync($"/api/incidents/{id}/hold", new { hold = false })
        ).EnsureSuccessStatusCode();
        (await owner.DeleteAsync($"/api/incidents/{id}")).EnsureSuccessStatusCode();
        var live = await owner.GetFromJsonAsync<JsonElement>($"/api/incidents?siteId={siteId}");
        Assert.Equal(0, live.GetProperty("total").GetInt32());
        var trash = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/incidents?siteId={siteId}&trash=true"
        );
        Assert.Equal(1, trash.GetProperty("total").GetInt32());
        (await owner.PostAsync($"/api/incidents/{id}/restore", null)).EnsureSuccessStatusCode();
        detail = await owner.GetFromJsonAsync<JsonElement>($"/api/incidents/{id}");
        Assert.True(detail.GetProperty("deletedAt").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Stats_roll_up_on_the_stamped_business_date()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var siteId = await CreateSiteAsync(owner, "Stats Store", "America/Los_Angeles");
        // 2026-05-10T05:30Z is still 2026-05-09 in Los Angeles (UTC-7 in May)
        foreach (
            var (category, severity, at, loss) in new[]
            {
                ("Theft", "Low", "2026-05-10T05:30:00Z", 40m),
                ("Theft", "High", "2026-05-10T20:00:00Z", 900m),
                ("Vandalism", "Medium", "2026-05-11T12:00:00Z", 0m),
            }
        )
            (
                await owner.PostAsJsonAsync(
                    "/api/incidents",
                    new
                    {
                        siteId,
                        category,
                        severity,
                        title = category,
                        occurredAt = DateTimeOffset.Parse(at),
                        lossAmount = loss,
                    }
                )
            ).EnsureSuccessStatusCode();

        var stats = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/incidents/stats?siteId={siteId}&from=2026-05-09&to=2026-05-11"
        );
        Assert.Equal(3, stats.GetProperty("total").GetInt32());
        Assert.Equal(940m, stats.GetProperty("totalLoss").GetDecimal());
        var byDate = stats
            .GetProperty("byBusinessDate")
            .EnumerateArray()
            .ToDictionary(
                d => d.GetProperty("key").GetString()!,
                d => d.GetProperty("count").GetInt32()
            );
        Assert.Equal(
            new Dictionary<string, int>
            {
                ["2026-05-09"] = 1,
                ["2026-05-10"] = 1,
                ["2026-05-11"] = 1,
            },
            byDate
        );
        var theft = stats
            .GetProperty("byCategory")
            .EnumerateArray()
            .Single(c => c.GetProperty("key").GetString() == "Theft");
        Assert.Equal(2, theft.GetProperty("count").GetInt32());
        Assert.Equal(940m, theft.GetProperty("loss").GetDecimal());

        // time-of-day heat is stamped in the SITE's zone: 05:30Z on the 10th is
        // 22:xx on Saturday the 9th in Los Angeles
        var byHour = stats
            .GetProperty("byHour")
            .EnumerateArray()
            .ToDictionary(
                h => h.GetProperty("key").GetString()!,
                h => h.GetProperty("count").GetInt32()
            );
        Assert.Equal(1, byHour["22"]);
        Assert.Equal(1, byHour["13"]); // 20:00Z on the 10th
        Assert.Equal(1, byHour["05"]); // 12:00Z on the 11th
        var byWeekday = stats
            .GetProperty("byWeekday")
            .EnumerateArray()
            .ToDictionary(
                d => d.GetProperty("key").GetString()!,
                d => d.GetProperty("count").GetInt32()
            );
        Assert.Equal(1, byWeekday["6"]); // Saturday the 9th
        Assert.Equal(1, byWeekday["7"]); // Sunday the 10th
        Assert.Equal(1, byWeekday["1"]); // Monday the 11th

        // the window excludes the first one when it starts on the 10th
        var narrower = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/incidents/stats?siteId={siteId}&from=2026-05-10&to=2026-05-11"
        );
        Assert.Equal(2, narrower.GetProperty("total").GetInt32());
    }

    private static async Task<Guid> CreateSiteAsync(HttpClient client, string name, string timeZone)
    {
        var hierarchy = await client.GetAsync("/api/hierarchy");
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
            var created = await client.PostAsJsonAsync(
                "/api/hierarchy",
                new { name = "Org A", levels = new[] { "Region" } }
            );
            created.EnsureSuccessStatusCode();
            rootId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("rootNodeId")
                .GetGuid();
        }
        var site = await client.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name,
                timeZone,
            }
        );
        site.EnsureSuccessStatusCode();
        return (await site.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string name)
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
        var created = await client.PostAsJsonAsync(
            "/api/files",
            new
            {
                name,
                contentType = "image/jpeg",
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
