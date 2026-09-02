using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace LocIntel.IntegrationTests;

/// <summary>ADR 18: staging + dry-run diff + idempotent commit, uploads and connectors on one core.</summary>
public class IngestTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private async Task<(HttpClient client, Guid rootId, Guid eastId)> Setup()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserA);
        var get = await client.GetAsync("/api/hierarchy");
        Guid rootId;
        if (get.StatusCode == HttpStatusCode.OK)
        {
            var tree = await get.Content.ReadFromJsonAsync<JsonElement>();
            rootId = tree.GetProperty("nodes")
                .EnumerateArray()
                .First(n => n.GetProperty("depth").GetInt32() == 0)
                .GetProperty("id")
                .GetGuid();
            var east = tree.GetProperty("nodes")
                .EnumerateArray()
                .FirstOrDefault(n => n.GetProperty("name").GetString() == "IngestEast");
            if (east.ValueKind == JsonValueKind.Object)
                return (client, rootId, east.GetProperty("id").GetGuid());
        }
        else
        {
            var created = await client.PostAsJsonAsync(
                "/api/hierarchy",
                new { name = "Org A", levels = new[] { "Region", "Market" } }
            );
            created.EnsureSuccessStatusCode();
            rootId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("rootNodeId")
                .GetGuid();
        }
        var node = await client.PostAsJsonAsync(
            "/api/hierarchy/nodes",
            new { parentId = rootId, name = "IngestEast" }
        );
        node.EnsureSuccessStatusCode();
        return (
            client,
            rootId,
            (await node.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()
        );
    }

    private async Task<Guid> UploadCsv(HttpClient client, string csv)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        var created = await client.PostAsJsonAsync(
            "/api/files",
            new
            {
                name = "sites.csv",
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

        // staging requires a Clean verdict
        for (var i = 0; i < 200; i++)
        {
            var files = await ApiFixture.GetItemsAsync(client, "/api/files");
            if (
                files
                    .EnumerateArray()
                    .First(f => f.GetProperty("id").GetGuid() == fileId)
                    .GetProperty("status")
                    .GetString() == "Clean"
            )
                break;
            await Task.Delay(100);
        }
        return fileId;
    }

    private static async Task<JsonElement> Stage(HttpClient client, Guid fileId)
    {
        var staged = await client.PostAsJsonAsync("/api/ingest/uploads", new { fileId });
        Assert.True(staged.IsSuccessStatusCode, await staged.Content.ReadAsStringAsync());
        return await staged.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement?> PollSite(HttpClient client, string name, bool expect = true)
    {
        for (var i = 0; i < 200; i++)
        {
            var sites = await ApiFixture.GetItemsAsync(client, "/api/sites");
            var match = sites
                .EnumerateArray()
                .FirstOrDefault(s => s.GetProperty("name").GetString() == name);
            if ((match.ValueKind == JsonValueKind.Object) == expect)
                return match.ValueKind == JsonValueKind.Object ? match : null;
            await Task.Delay(100);
        }
        return null;
    }

    [Fact]
    public async Task Upload_preview_commit_creates_updates_and_closes()
    {
        var (client, _, _) = await Setup();

        // round 1: two creates
        var csv1 = """
            external_id,name,time_zone,node,status
            ing-001,Downtown,America/New_York,IngestEast,open
            ing-002,Uptown,America/Chicago,IngestEast,open
            """;
        var batch1 = await Stage(client, await UploadCsv(client, csv1));
        Assert.Equal(2, batch1.GetProperty("counts").GetProperty("create").GetInt32());
        var commit1 = await client.PostAsync(
            $"/api/ingest/batches/{batch1.GetProperty("batchId").GetGuid()}/commit",
            null
        );
        commit1.EnsureSuccessStatusCode();
        Assert.NotNull(await PollSite(client, "Downtown"));

        // round 2: same file re-run = all unchanged (idempotent by external id)
        var batch2 = await Stage(client, await UploadCsv(client, csv1));
        Assert.Equal(0, batch2.GetProperty("counts").GetProperty("create").GetInt32());
        Assert.Equal(2, batch2.GetProperty("counts").GetProperty("unchanged").GetInt32());

        // round 3: rename one, close the other
        var csv3 = """
            external_id,name,time_zone,node,status
            ing-001,Downtown Flagship,America/New_York,IngestEast,open
            ing-002,Uptown,America/Chicago,IngestEast,closed
            """;
        var batch3 = await Stage(client, await UploadCsv(client, csv3));
        Assert.Equal(1, batch3.GetProperty("counts").GetProperty("update").GetInt32());
        Assert.Equal(1, batch3.GetProperty("counts").GetProperty("close").GetInt32());
        (
            await client.PostAsync(
                $"/api/ingest/batches/{batch3.GetProperty("batchId").GetGuid()}/commit",
                null
            )
        ).EnsureSuccessStatusCode();

        var renamed = await PollSite(client, "Downtown Flagship");
        Assert.NotNull(renamed);
        JsonElement? uptown = null;
        for (var i = 0; i < 200 && uptown?.GetProperty("status").GetString() != "Closed"; i++)
        {
            await Task.Delay(100);
            uptown = await PollSite(client, "Uptown");
        }
        Assert.Equal("Closed", uptown?.GetProperty("status").GetString());

        // closing is a DOMAIN EVENT (ADR 18), never a delete
        List<LocIntel.Modules.Audit.Data.DomainLogEntry> closures = [];
        for (var i = 0; i < 200 && closures.Count == 0; i++)
        {
            await Task.Delay(100);
            closures = await fixture.QueryAudit(db =>
                Microsoft
                    .EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(
                        db.DomainEvents
                    )
                    .Where(a => a.EventName == "site.closed")
            );
        }
        Assert.NotEmpty(closures);
    }

    [Fact]
    public async Task Invalid_rows_are_reported_not_applied()
    {
        var (client, _, _) = await Setup();
        var csv = """
            external_id,name,time_zone,node,status
            ,Missing Id,America/New_York,IngestEast,open
            ing-bad-tz,Bad Zone,Mars/Olympus,IngestEast,open
            ing-bad-node,Lost,America/New_York,Nowhere/AtAll,open
            """;
        var batch = await Stage(client, await UploadCsv(client, csv));
        Assert.Equal(3, batch.GetProperty("counts").GetProperty("invalid").GetInt32());

        var preview = await client.GetFromJsonAsync<JsonElement>(
            $"/api/ingest/batches/{batch.GetProperty("batchId").GetGuid()}"
        );
        var badZone = preview
            .GetProperty("rows")
            .EnumerateArray()
            .First(r => r.GetProperty("externalId").GetString() == "ing-bad-tz");
        Assert.Contains("IANA", badZone.GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task Connector_syncs_through_the_same_staging_core()
    {
        var (client, _, _) = await Setup();

        // stub source: an org's POS system speaking json over http with an api key
        string? seenKey = null;
        var stub = WebApplication.CreateSlimBuilder().Build();
        // ephemeral port: parallel test CLASSES each run stubs, and the
        // default :5000 collides across them (found as intermittent
        // AddressInUse failures once a second stub-using class existed)
        stub.Urls.Add("http://127.0.0.1:0");
        stub.MapGet(
            "/sites",
            (HttpRequest req) =>
            {
                seenKey = req.Headers["X-Api-Key"].ToString();
                return Results.Json(
                    new[]
                    {
                        new
                        {
                            external_id = "pos-100",
                            name = "Harborside",
                            time_zone = "America/New_York",
                            node = "IngestEast",
                            status = "open",
                        },
                    }
                );
            }
        );
        await stub.StartAsync();
        var stubUrl = stub.Urls.First();

        try
        {
            var created = await client.PostAsJsonAsync(
                "/api/connectors",
                new
                {
                    name = "pos-sync",
                    url = $"{stubUrl}/sites",
                    apiKey = "sk-pos-secret",
                }
            );
            created.EnsureSuccessStatusCode();
            var connectorId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id")
                .GetGuid();

            (
                await client.PostAsync($"/api/connectors/{connectorId}/sync", null)
            ).EnsureSuccessStatusCode();

            // sync lands a STAGED batch (same core as uploads); commit stays explicit
            JsonElement batch = default;
            for (var i = 0; i < 200; i++)
            {
                await Task.Delay(100);
                var found = await fixture.QueryIngestBatch("pos-sync");
                if (found is { } b)
                {
                    batch = JsonSerializer.SerializeToElement(b);
                    break;
                }
            }
            Assert.Equal("sk-pos-secret", seenKey); // decrypted credential reached the source
            var batchId = batch.GetProperty("Id").GetGuid();
            var commit = await client.PostAsync($"/api/ingest/batches/{batchId}/commit", null);
            commit.EnsureSuccessStatusCode();
            var site = await PollSite(client, "Harborside");
            if (site is null)
            {
                var preview = await client.GetStringAsync($"/api/ingest/batches/{batchId}");
                Assert.Fail(
                    $"no site; commit={await commit.Content.ReadAsStringAsync()} preview={preview}"
                );
            }

            // credential access is audited (ADR 31)
            List<LocIntel.Modules.Audit.Data.DomainLogEntry> accesses = [];
            for (var i = 0; i < 200 && accesses.Count == 0; i++)
            {
                await Task.Delay(100);
                accesses = await fixture.QueryAudit(db =>
                    Microsoft
                        .EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(
                            db.DomainEvents
                        )
                        .Where(a => a.EventName == "connector.credentials_accessed")
                );
            }
            Assert.NotEmpty(accesses);
        }
        finally
        {
            await stub.StopAsync();
        }
    }

    [Fact]
    public async Task Staged_batches_list_and_discard_but_committed_history_stays()
    {
        var (client, _, _) = await Setup();
        var csv = """
            external_id,name,time_zone,node,status
            disc-001,Discardable,America/New_York,IngestEast,open
            """;
        var batchId = (await Stage(client, await UploadCsv(client, csv)))
            .GetProperty("batchId")
            .GetGuid();

        var listed = await client.GetFromJsonAsync<JsonElement>("/api/ingest/batches");
        var row = listed.EnumerateArray().First(b => b.GetProperty("id").GetGuid() == batchId);
        Assert.Equal("Staged", row.GetProperty("status").GetString());
        Assert.Equal(1, row.GetProperty("counts").GetProperty("create").GetInt32());

        // discard: nothing applied, the diff rows are gone, the record stays
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync($"/api/ingest/batches/{batchId}/discard", null)).StatusCode
        );
        var after = await client.GetFromJsonAsync<JsonElement>($"/api/ingest/batches/{batchId}");
        Assert.Equal("Discarded", after.GetProperty("status").GetString());
        Assert.Equal(0, after.GetProperty("rows").GetArrayLength());

        // a discarded batch neither commits nor discards again
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await client.PostAsync($"/api/ingest/batches/{batchId}/commit", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await client.PostAsync($"/api/ingest/batches/{batchId}/discard", null)).StatusCode
        );
        Assert.Null(await PollSite(client, "Discardable", expect: false));
    }

    [Fact]
    public async Task Connector_update_rewraps_key_and_delete_removes_it()
    {
        var (client, _, _) = await Setup();
        string? seenKey = null;
        var stub = WebApplication.CreateSlimBuilder().Build();
        // ephemeral port: parallel test CLASSES each run stubs, and the
        // default :5000 collides across them (found as intermittent
        // AddressInUse failures once a second stub-using class existed)
        stub.Urls.Add("http://127.0.0.1:0");
        stub.MapGet(
            "/sites",
            (HttpRequest req) =>
            {
                seenKey = req.Headers["X-Api-Key"].ToString();
                return Results.Json(Array.Empty<object>());
            }
        );
        await stub.StartAsync();
        try
        {
            var created = await client.PostAsJsonAsync(
                "/api/connectors",
                new
                {
                    name = "editable",
                    url = $"{stub.Urls.First()}/sites",
                    apiKey = "key-one",
                }
            );
            created.EnsureSuccessStatusCode();
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id")
                .GetGuid();

            // the inventory shows config, never credentials
            var listed = await client.GetFromJsonAsync<JsonElement>("/api/connectors");
            var row = listed.EnumerateArray().First(c => c.GetProperty("id").GetGuid() == id);
            Assert.Equal(JsonValueKind.Null, row.GetProperty("syncIntervalHours").ValueKind);
            Assert.False(row.TryGetProperty("encryptedCredentials", out _));
            Assert.False(row.TryGetProperty("apiKey", out _));

            // edit: new key rewraps, new schedule sticks
            var updated = await client.PutAsJsonAsync(
                $"/api/connectors/{id}",
                new
                {
                    name = "editable-v2",
                    url = $"{stub.Urls.First()}/sites",
                    apiKey = "key-two",
                    syncIntervalHours = 6,
                }
            );
            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
            (await client.PostAsync($"/api/connectors/{id}/sync", null)).EnsureSuccessStatusCode();
            for (var i = 0; i < 200 && seenKey is null; i++)
                await Task.Delay(100);
            Assert.Equal("key-two", seenKey);

            var relisted = await client.GetFromJsonAsync<JsonElement>("/api/connectors");
            var edited = relisted.EnumerateArray().First(c => c.GetProperty("id").GetGuid() == id);
            Assert.Equal("editable-v2", edited.GetProperty("name").GetString());
            Assert.Equal(6, edited.GetProperty("syncIntervalHours").GetInt32());

            // delete: gone from inventory, sync 404s
            Assert.Equal(
                HttpStatusCode.NoContent,
                (await client.DeleteAsync($"/api/connectors/{id}")).StatusCode
            );
            Assert.DoesNotContain(
                (await client.GetFromJsonAsync<JsonElement>("/api/connectors")).EnumerateArray(),
                c => c.GetProperty("id").GetGuid() == id
            );
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.PostAsync($"/api/connectors/{id}/sync", null)).StatusCode
            );
        }
        finally
        {
            await stub.StopAsync();
        }
    }

    [Fact]
    public async Task Scheduled_sweep_syncs_due_connectors_and_leaves_manual_ones_alone()
    {
        var (client, _, _) = await Setup();
        var stub = WebApplication.CreateSlimBuilder().Build();
        // ephemeral port: parallel test CLASSES each run stubs, and the
        // default :5000 collides across them (found as intermittent
        // AddressInUse failures once a second stub-using class existed)
        stub.Urls.Add("http://127.0.0.1:0");
        stub.MapGet(
            "/sites",
            () =>
                Results.Json(
                    new[]
                    {
                        new
                        {
                            external_id = "sched-1",
                            name = "Scheduled Site",
                            time_zone = "America/New_York",
                            node = "IngestEast",
                            status = "open",
                        },
                    }
                )
        );
        await stub.StartAsync();
        try
        {
            foreach (
                var (name, interval) in new (string, int?)[]
                {
                    ("scheduled-conn", 1),
                    ("manual-conn", null),
                }
            )
                (
                    await client.PostAsJsonAsync(
                        "/api/connectors",
                        new
                        {
                            name,
                            url = $"{stub.Urls.First()}/sites",
                            apiKey = "k",
                            syncIntervalHours = interval,
                        }
                    )
                ).EnsureSuccessStatusCode();

            // the hourly enumerator's per-org sweep, delivered by hand
            await fixture.PublishForOrgA(new LocIntel.Modules.Ingest.SyncDueConnectors());

            // the due connector lands a STAGED batch (never auto-committed)...
            JsonElement batches = default;
            var found = false;
            for (var i = 0; i < 200 && !found; i++)
            {
                batches = await client.GetFromJsonAsync<JsonElement>("/api/ingest/batches");
                found = batches
                    .EnumerateArray()
                    .Any(b => b.GetProperty("source").GetString() == "scheduled-conn");
                if (!found)
                    await Task.Delay(100);
            }
            Assert.True(found, "scheduled connector never produced a batch");
            Assert.Equal(
                "Staged",
                batches
                    .EnumerateArray()
                    .First(b => b.GetProperty("source").GetString() == "scheduled-conn")
                    .GetProperty("status")
                    .GetString()
            );

            // ...the manual one is untouched
            Assert.DoesNotContain(
                batches.EnumerateArray(),
                b => b.GetProperty("source").GetString() == "manual-conn"
            );

            // a second sweep inside the interval is a no-op (LastSyncedAt advanced)
            await fixture.PublishForOrgA(new LocIntel.Modules.Ingest.SyncDueConnectors());
            await Task.Delay(500);
            Assert.Equal(
                1,
                (await client.GetFromJsonAsync<JsonElement>("/api/ingest/batches"))
                    .EnumerateArray()
                    .Count(b => b.GetProperty("source").GetString() == "scheduled-conn")
            );
        }
        finally
        {
            await stub.StopAsync();
        }
    }
}
