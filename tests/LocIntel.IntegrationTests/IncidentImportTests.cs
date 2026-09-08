using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Npgsql;

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
        using var otherOrg = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await otherOrg.PostAsync($"/api/incidents/imports/{batchId}/commit", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await otherOrg.PostAsync($"/api/incidents/imports/{batchId}/discard", null)).StatusCode
        );
        using var readOnly = await fixture.LoginAsync(ApiFixture.ViewerA);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await readOnly.PostAsync($"/api/incidents/imports/{batchId}/commit", null)).StatusCode
        );

        // Fail the batch status write after incident insertion; both must roll back.
        await using var admin = new NpgsqlConnection(fixture.PostgresConnectionString);
        await admin.OpenAsync();
        await using var failCommit = new NpgsqlCommand(
            "ALTER TABLE incidents.import_batches ADD CONSTRAINT reject_commit CHECK (status <> 'Committed') NOT VALID",
            admin
        );
        await failCommit.ExecuteNonQueryAsync();
        try
        {
            Assert.Equal(
                HttpStatusCode.InternalServerError,
                (await owner.PostAsync($"/api/incidents/imports/{batchId}/commit", null)).StatusCode
            );
            var unchanged = await owner.GetFromJsonAsync<JsonElement>(
                $"/api/incidents/imports/{batchId}"
            );
            Assert.Equal(
                "Staged",
                unchanged.GetProperty("batch").GetProperty("status").GetString()
            );
            Assert.All(
                unchanged.GetProperty("rows").EnumerateArray(),
                row => Assert.Equal(JsonValueKind.Null, row.GetProperty("incidentId").ValueKind)
            );
            Assert.Equal(
                0,
                (await owner.GetFromJsonAsync<JsonElement>($"/api/incidents?siteId={siteId}"))
                    .GetProperty("total")
                    .GetInt32()
            );
        }
        finally
        {
            await using var restore = new NpgsqlCommand(
                "ALTER TABLE incidents.import_batches DROP CONSTRAINT reject_commit",
                admin
            );
            await restore.ExecuteNonQueryAsync();
        }
        var attempts = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(_ => owner.PostAsync($"/api/incidents/imports/{batchId}/commit", null))
        );
        var committed = Assert.Single(attempts, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(
            attempts.Where(r => r != committed),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode)
        );
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

        // A live event proves the alert pipeline is active; historical high-severity rows stay quiet.
        var live = await owner.PostAsJsonAsync(
            "/api/incidents",
            new
            {
                siteId,
                category = "Theft",
                severity = "Critical",
                title = "Live import control",
                occurredAt = DateTimeOffset.UtcNow,
            }
        );
        live.EnsureSuccessStatusCode();
        var liveId = (await live.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var importedId = landed.GetProperty("id").GetGuid();
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                var alerts = (await owner.GetFromJsonAsync<JsonElement>("/api/alerts"))
                    .GetProperty("items")
                    .EnumerateArray()
                    .ToArray();
                Assert.DoesNotContain(
                    alerts,
                    a =>
                        a.GetProperty("incidentId").ValueKind == JsonValueKind.String
                        && a.GetProperty("incidentId").GetGuid() == importedId
                );
                return alerts.Any(a =>
                    a.GetProperty("incidentId").ValueKind == JsonValueKind.String
                    && a.GetProperty("incidentId").GetGuid() == liveId
                );
            },
            "the live control incident alert"
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

    [Fact]
    public async Task Commit_rechecks_the_current_callers_site_scope()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var rootId = await ApiFixture.EnsureRootAsync(owner);
        var outsideId = await ApiFixture.EnsureSiteAsync(owner, "Outside import scope");
        var node = await owner.PostAsJsonAsync(
            "/api/hierarchy/nodes",
            new { parentId = rootId, name = "Import scope" }
        );
        node.EnsureSuccessStatusCode();
        var branch = await node.Content.ReadFromJsonAsync<JsonElement>();
        var site = await owner.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = branch.GetProperty("id").GetGuid(),
                name = "Inside import scope",
                timeZone = "Etc/UTC",
            }
        );
        site.EnsureSuccessStatusCode();
        var insideId = (await site.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var role = await owner.PostAsJsonAsync(
            "/api/roles",
            new
            {
                name = "Scoped importer",
                grants = new[] { new { domain = "incidents", action = "manage" } },
            }
        );
        role.EnsureSuccessStatusCode();
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var userId = await fixture.CreateMemberAsync(
            "scoped-importer@locintel.local",
            fixture.OrgA
        );
        (
            await owner.PostAsJsonAsync(
                $"/api/roles/{roleId}/assign",
                new { userId, scopePath = branch.GetProperty("path").GetString() }
            )
        ).EnsureSuccessStatusCode();
        var fileId = await UploadAsync(
            owner,
            "scope-history.csv",
            Encoding.UTF8.GetBytes(
                $"site,occurred_at,category,severity,title\nOutside import scope,2026-01-01T00:00:00Z,Theft,High,Outside\nInside import scope,2026-01-01T00:00:00Z,Theft,High,Inside"
            )
        );
        var staged = await owner.PostAsJsonAsync("/api/incidents/imports", new { fileId });
        staged.EnsureSuccessStatusCode();
        var batch = await staged.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, batch.GetProperty("valid").GetInt32());
        var id = batch.GetProperty("id").GetGuid();
        using var scoped = await fixture.LoginAsync("scoped-importer@locintel.local");
        var committed = await scoped.PostAsync($"/api/incidents/imports/{id}/commit", null);
        committed.EnsureSuccessStatusCode();
        Assert.Equal(
            1,
            (await committed.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("created")
                .GetInt32()
        );
        Assert.Equal(
            0,
            (await owner.GetFromJsonAsync<JsonElement>($"/api/incidents?siteId={outsideId}"))
                .GetProperty("total")
                .GetInt32()
        );
        Assert.Equal(
            1,
            (await owner.GetFromJsonAsync<JsonElement>($"/api/incidents?siteId={insideId}"))
                .GetProperty("total")
                .GetInt32()
        );
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/incidents/imports/{id}");
        Assert.Single(
            detail.GetProperty("rows").EnumerateArray(),
            r => r.GetProperty("incidentId").ValueKind == JsonValueKind.String
        );
    }

    [Fact]
    public async Task Commit_and_discard_cannot_both_win()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var siteId = await ApiFixture.EnsureSiteAsync(owner, "Import race site");
        var fileId = await UploadAsync(
            owner,
            "race.csv",
            Encoding.UTF8.GetBytes(
                $"site,occurred_at,category,severity,title\nImport race site,2026-01-01T00:00:00Z,Theft,High,Race"
            )
        );
        var staged = await owner.PostAsJsonAsync("/api/incidents/imports", new { fileId });
        staged.EnsureSuccessStatusCode();
        var id = (await staged.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var attempts = await Task.WhenAll(
            owner.PostAsync($"/api/incidents/imports/{id}/commit", null),
            owner.PostAsync($"/api/incidents/imports/{id}/discard", null)
        );
        Assert.Single(attempts, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(attempts, r => r.StatusCode == HttpStatusCode.Conflict);
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/incidents/imports/{id}");
        var committed =
            detail.GetProperty("batch").GetProperty("status").GetString() == "Committed";
        Assert.Equal(committed ? 1 : 0, detail.GetProperty("rows").GetArrayLength());
        Assert.Equal(
            committed ? 1 : 0,
            (await owner.GetFromJsonAsync<JsonElement>($"/api/incidents?siteId={siteId}"))
                .GetProperty("total")
                .GetInt32()
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
        await ApiFixture.WaitUntilAsync(
            async () =>
                (await client.GetFromJsonAsync<JsonElement>($"/api/files/{fileId}"))
                    .GetProperty("status")
                    .GetString() == "Clean",
            "incident import upload to finish its asynchronous scan"
        );
        return fileId;
    }
}
