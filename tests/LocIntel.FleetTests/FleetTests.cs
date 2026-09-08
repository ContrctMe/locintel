using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace LocIntel.FleetTests;

/// <summary>
/// What only shows with two: the proofs a single-process suite cannot give.
/// Cases run in name order (NameOrderer); the one that kills a replica comes
/// last, so the ones before it see the whole fleet.
/// </summary>
public sealed class FleetTests(Fleet fleet) : IClassFixture<Fleet>
{
    [FleetFact]
    public async Task A_one_session_is_answered_by_every_replica()
    {
        var alice = await fleet.LoginAsync(Fleet.Alice);
        var instances = new HashSet<string>();
        for (var i = 0; i < fleet.Replicas * 6; i++)
        {
            var response = await alice.GetAsync("/me");
            response.EnsureSuccessStatusCode();
            instances.Add(Fleet.InstanceOf(response));
        }
        // the cookie session is state on the database, not on the replica that made it
        Assert.Equal(fleet.Replicas, instances.Count);
    }

    [FleetFact]
    public async Task B_an_idempotency_key_holds_across_replicas()
    {
        var alice = await fleet.LoginAsync(Fleet.Alice);
        var root = await Fleet.RootNodeAsync(alice);
        var name = $"Fleet idempotent {Guid.NewGuid():N}";
        var key = Guid.NewGuid().ToString();
        var answered = new HashSet<string>();
        var ids = new HashSet<Guid>();
        for (var i = 0; i < fleet.Replicas * 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sites")
            {
                Content = JsonContent.Create(
                    new
                    {
                        nodeId = root,
                        name,
                        timeZone = "Etc/UTC",
                    }
                ),
            };
            request.Headers.Add("Idempotency-Key", key);
            var response = await alice.SendAsync(request);
            answered.Add(Fleet.InstanceOf(response));
            // a retry mid-flight is 409 (the original is still running); every settled answer is the one site
            if (response.StatusCode == HttpStatusCode.Conflict)
                continue;
            ids.Add((await Fleet.JsonAsync(response)).GetProperty("id").GetGuid());
        }
        Assert.Single(ids);
        var hits = (
            await Fleet.JsonAsync(
                await alice.GetAsync($"/api/sites?q={Uri.EscapeDataString(name)}&limit=10")
            )
        )
            .GetProperty("total")
            .GetInt32();
        Assert.Equal(1, hits);
        Assert.True(
            answered.Count > 1 || fleet.Replicas == 1,
            "the key was only ever tried on one replica"
        );
    }

    // Gateway fairness is integration-proven by tools/gateway.test.mjs (ADR 54).

    [FleetFact]
    public async Task C_concurrent_creates_share_one_remaining_business_slot()
    {
        using var alice = await fleet.LoginAsync(Fleet.Alice);
        using var op = await fleet.LoginAsync(Fleet.Operator);
        var orgId = (await Fleet.JsonAsync(await alice.GetAsync("/me")))
            .GetProperty("activeOrg")
            .GetGuid();
        var original = (await Fleet.JsonAsync(await alice.GetAsync("/api/entitlements")))
            .GetProperty("sites.max")
            .GetProperty("value")
            .GetString()!;
        var before = (await Fleet.JsonAsync(await alice.GetAsync("/api/sites?limit=1")))
            .GetProperty("total")
            .GetInt32();
        var root = await Fleet.RootNodeAsync(alice);
        var endpoint = $"/api/operator/orgs/{orgId}/entitlements/sites.max";
        (
            await op.PutAsJsonAsync(endpoint, new { value = (before + 1).ToString() })
        ).EnsureSuccessStatusCode();
        try
        {
            var instances = new System.Collections.Concurrent.ConcurrentBag<string>();
            var outcomes = await Task.WhenAll(
                Enumerable
                    .Range(0, fleet.Replicas * 4)
                    .Select(async i =>
                    {
                        HttpStatusCode outcome = default;
                        await Fleet.WaitUntilAsync(
                            async () =>
                            {
                                using var response = await alice.PostAsJsonAsync(
                                    "/api/sites",
                                    new
                                    {
                                        nodeId = root,
                                        name = $"Capacity {i}",
                                        timeZone = "Etc/UTC",
                                    }
                                );
                                instances.Add(Fleet.InstanceOf(response));
                                outcome = response.StatusCode;
                                return outcome != HttpStatusCode.ServiceUnavailable;
                            },
                            "site admission contention to clear"
                        );
                        return outcome;
                    })
            );
            Assert.Single(outcomes, status => status == HttpStatusCode.OK);
            Assert.All(
                outcomes,
                status =>
                    Assert.Contains(
                        status,
                        new[] { HttpStatusCode.OK, HttpStatusCode.PaymentRequired }
                    )
            );
            Assert.Equal(
                before + 1,
                (await Fleet.JsonAsync(await alice.GetAsync("/api/sites?limit=1")))
                    .GetProperty("total")
                    .GetInt32()
            );
            Assert.Equal(fleet.Replicas, instances.Distinct().Count());
        }
        finally
        {
            (await op.PutAsJsonAsync(endpoint, new { value = original })).EnsureSuccessStatusCode();
        }
    }

    [FleetFact]
    public async Task D_each_sweep_runs_once_per_period_across_workers()
    {
        foreach (var port in fleet.WorkerPorts)
        {
            using var probe = new HttpClient();
            var health = await probe.GetStringAsync($"http://127.0.0.1:{port}/healthz");
            Assert.Contains("\"status\":\"ok\"", health);
            Assert.Contains("\"role\":\"worker\"", health);
        }
        await using var db = new NpgsqlConnection(fleet.OwnerConnectionString);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*), count(DISTINCT (sweep, period)), count(DISTINCT claimed_by)
            FROM platform.sweep_runs
            """,
            db
        );
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var rows = reader.GetInt64(0);
        var periods = reader.GetInt64(1);
        var claimants = reader.GetInt64(2);
        Assert.True(rows > 0, "no sweep has claimed a period yet");
        Assert.Equal(periods, rows); // one claim per period, whichever worker won
        Assert.True(
            claimants >= 1 && claimants <= fleet.Replicas,
            $"claimed by {claimants} processes"
        );
    }

    [FleetFact]
    public async Task E_messages_survive_the_death_of_the_replica_that_took_them()
    {
        if (fleet.Replicas < 2)
            return; // one replica has nobody to hand over to
        var alice = await fleet.LoginAsync(Fleet.Alice);
        // the seeded plan allows a hundred sites; the batch is bigger than that on purpose
        var op = await fleet.LoginAsync(Fleet.Operator);
        var orgId = (await Fleet.JsonAsync(await alice.GetAsync("/me")))
            .GetProperty("activeOrg")
            .GetGuid();
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await op.PutAsJsonAsync(
                    $"/api/operator/orgs/{orgId}/entitlements/sites.max",
                    new { value = "100000" }
                )
            ).StatusCode
        );
        var stamp = Guid.NewGuid().ToString("N")[..8];
        const int rows = 400;
        var csv = new StringBuilder("external_id,name,time_zone,node,status\n");
        for (var i = 1; i <= rows; i++)
            csv.Append($"fleet-{stamp}-{i:0000},Fleet{stamp} {i:0000},Etc/UTC,,open\n");
        var fileId = await UploadAsync(alice, csv.ToString());
        var batch = await Fleet.JsonAsync(
            await alice.PostAsJsonAsync("/api/ingest/uploads", new { fileId })
        );
        Assert.Equal(rows, batch.GetProperty("counts").GetProperty("create").GetInt32());

        // the commit publishes one durable message per row into the local queue
        // of the replica that answered; killing it mid-batch is the hand-over test
        var commit = await alice.PostAsync(
            $"/api/ingest/batches/{batch.GetProperty("batchId").GetGuid()}/commit",
            null
        );
        commit.EnsureSuccessStatusCode();
        var instance = Fleet.InstanceOf(commit);
        var pid = int.Parse(instance[(instance.LastIndexOf(':') + 1)..]);
        Process.GetProcessById(pid).Kill();

        await Fleet.WaitUntilAsync(
            async () =>
            {
                var page = await alice.GetAsync($"/api/sites?q=Fleet{stamp}&limit=1");
                return page.IsSuccessStatusCode
                    && (await page.Content.ReadFromJsonAsync<JsonElement>())
                        .GetProperty("total")
                        .GetInt32() == rows;
            },
            $"all {rows} sites to be applied after replica {instance} died",
            TimeSpan.FromMinutes(5)
        );
        // and the survivors still answer every request
        for (var i = 0; i < 5; i++)
            (await alice.GetAsync("/me")).EnsureSuccessStatusCode();
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string csv)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        var created = await Fleet.JsonAsync(
            await client.PostAsJsonAsync(
                "/api/files",
                new
                {
                    name = "fleet.csv",
                    contentType = "text/csv",
                    sizeBytes = bytes.Length,
                }
            )
        );
        var fileId = created.GetProperty("fileId").GetGuid();
        var ticket = created.GetProperty("ticket").GetProperty("url").GetString()!;
        var put = new HttpRequestMessage(HttpMethod.Put, ticket)
        {
            Content = new ByteArrayContent(bytes),
        };
        (await client.SendAsync(put)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/files/{fileId}/complete", null)).EnsureSuccessStatusCode();
        await Fleet.WaitUntilAsync(
            async () =>
                (await Fleet.JsonAsync(await client.GetAsync("/api/files?limit=200")))
                    .GetProperty("items")
                    .EnumerateArray()
                    .Any(f =>
                        f.GetProperty("id").GetGuid() == fileId
                        && f.GetProperty("status").GetString() == "Clean"
                    ),
            "the upload to be scanned clean"
        );
        return fileId;
    }
}
