using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LocIntel.Modules.Audit;
using LocIntel.Modules.Ingest;
using LocIntel.Modules.Storage;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using LocIntel.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Xunit.Abstractions;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Background sweeps only register in the WORKER role, so nothing else in
/// this suite resolves their dependencies - a missing registration would
/// surface as a production worker crashing on its first tick, hours after
/// deploy. These assert the graph directly.
/// </summary>
public class BackgroundSweepTests(ApiFixture fixture, ITestOutputHelper output)
    : IClassFixture<ApiFixture>
{
    [Fact]
    public void The_per_org_sweep_port_resolves()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var enumerator = scope.ServiceProvider.GetRequiredService<IOrganizationEnumerator>();
        Assert.NotNull(enumerator);
    }

    [Fact]
    public async Task The_sweep_port_lists_the_orgs_a_sweep_would_fan_out_to()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var enumerator = scope.ServiceProvider.GetRequiredService<IOrganizationEnumerator>();
        var ids = await enumerator.ListIdsAsync();
        Assert.Contains(fixture.OrgA, ids);
        Assert.Contains(fixture.OrgB, ids);
    }

    [ScaleFact]
    [Trait("Category", "Scale")]
    public async Task Scale_baseline()
    {
        using var op = await fixture.OperatorClient();
        (
            await op.PutAsJsonAsync(
                $"/api/operator/orgs/{fixture.OrgA.Value}/entitlements/api.requests_per_minute",
                new { value = "1000000" }
            )
        ).EnsureSuccessStatusCode();
        var quota = fixture.Factory.Services.GetRequiredService<LocIntel.Api.OrgRateLimitCache>();
        await ApiFixture.WaitUntilAsync(
            () => Task.FromResult(quota.LimitFor(fixture.OrgA) == 1_000_000),
            "benchmark org quota to resolve before its first tenant request"
        );
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var root = await ApiFixture.EnsureRootAsync(owner);
        await SeedAsync(root);

        output.WriteLine("target | median ms | response bytes");
        await ReportAsync(owner, "/api/sites?limit=50");
        await ReportAsync(owner, "/api/sites?limit=50&offset=950");
        await ReportAsync(owner, "/api/listings/feed", 5);
        await ReportAsync(owner, "/api/audit/changes?limit=500");

        var guest = fixture.GuestClient();
        guest.DefaultRequestHeaders.Add("X-Forwarded-Host", "org-a.localhost");
        await ReportAsync(guest, "/public/sites", 5);
        await ReportAsync(guest, "/public/sites?near=42.36,-71.05", 5);

        using var backgroundDone = new CancellationTokenSource();
        var traffic = MeasureMixedAsync(owner, guest, backgroundDone.Token);
        var trafficErrors = 0;
        try
        {
            var csv = new StringBuilder("external_id,name,time_zone,node,status\n");
            for (var i = 0; i < 10_000; i++)
                csv.Append("import-")
                    .Append(i)
                    .Append(",Imported ")
                    .Append(i)
                    .Append(",Etc/UTC,,open\n");
            var timer = Stopwatch.StartNew();
            var rows = CsvParser.Parse(csv.ToString()).Select(CsvParser.ToSourceRow).ToList();
            timer.Stop();
            Assert.Equal(10_000, rows.Count);
            output.WriteLine(
                $"CSV parse (10,000 rows) | {timer.Elapsed.TotalMilliseconds:0.0} | {csv.Length}"
            );

            await using (var scope = fixture.Factory.Services.CreateAsyncScope())
            {
                scope
                    .ServiceProvider.GetRequiredService<TenantContext>()
                    .Set(fixture.OrgA, RegionId.Default);
                timer.Restart();
                var batch = await scope
                    .ServiceProvider.GetRequiredService<StagingService>()
                    .StageAsync(
                        fixture.OrgA,
                        await fixture.UserIdOf(ApiFixture.UserA),
                        "scale",
                        rows,
                        default
                    );
                timer.Stop();
                Assert.Contains("\"create\":10000", batch.Counts);
                output.WriteLine(
                    $"CSV stage (10,000 rows vs 1,000 sites) | {timer.Elapsed.TotalMilliseconds:0.0} | n/a"
                );
                timer.Restart();
                using var committed = await owner.PostAsync(
                    $"/api/ingest/batches/{batch.Id}/commit",
                    null
                );
                committed.EnsureSuccessStatusCode();
                Assert.Equal(
                    10_000,
                    (await committed.Content.ReadFromJsonAsync<JsonElement>())
                        .GetProperty("applied")
                        .GetInt32()
                );
                output.WriteLine(
                    $"CSV commit accepted (10,000 messages) | {timer.Elapsed.TotalMilliseconds:0.0} | n/a"
                );
                await ApiFixture.WaitUntilAsync(
                    async () =>
                    {
                        await using var connection = new Npgsql.NpgsqlConnection(
                            fixture.PostgresConnectionString
                        );
                        await connection.OpenAsync();
                        await using var count = connection.CreateCommand();
                        count.CommandText =
                            "SELECT count(*) FROM tenancy.sites WHERE org_id = @org AND external_id LIKE 'import-%'";
                        count.Parameters.AddWithValue("org", fixture.OrgA.Value);
                        return Equals(10_000L, await count.ExecuteScalarAsync());
                    },
                    "all 10,000 imported sites to be persisted",
                    TimeSpan.FromMinutes(3)
                );
                output.WriteLine(
                    $"CSV commit business completion (10,000 sites) | {timer.Elapsed.TotalMilliseconds:0.0} | {10_000 / timer.Elapsed.TotalSeconds:0.0} sites/s"
                );
            }

            await using (var scope = fixture.Factory.Services.CreateAsyncScope())
            {
                var orgs = scope.ServiceProvider.GetRequiredService<IOrganizationEnumerator>();
                timer.Restart();
                var ids = await orgs.ListIdsAsync();
                timer.Stop();
                Assert.True(ids.Count >= 1_000);
                output.WriteLine(
                    $"worker org enumeration ({ids.Count} orgs) | {timer.Elapsed.TotalMilliseconds:0.0} | n/a"
                );

                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();
                foreach (var org in ids)
                {
                    using var bytes = new MemoryStream([42]);
                    await store.WriteAsync(
                        $"scale-trash/{org.Value}",
                        bytes,
                        "application/octet-stream"
                    );
                }
                timer.Restart();
                foreach (var org in ids)
                    await bus.PublishForOrgAsync(org, new PurgeFileTrash());
                output.WriteLine(
                    $"worker durable fan-out ({ids.Count} messages) | {timer.Elapsed.TotalMilliseconds:0.0} | n/a"
                );
                await ApiFixture.WaitUntilAsync(
                    async () =>
                    {
                        await using var connection = new Npgsql.NpgsqlConnection(
                            fixture.PostgresConnectionString
                        );
                        await connection.OpenAsync();
                        await using var count = connection.CreateCommand();
                        count.CommandText =
                            "SELECT count(*) FROM storage.files WHERE key LIKE 'scale-trash/%' AND status = 'Erased'";
                        return Equals((long)ids.Count, await count.ExecuteScalarAsync());
                    },
                    "every seeded expired file to be erased",
                    TimeSpan.FromMinutes(3)
                );
                foreach (var org in ids)
                    Assert.Null(await store.GetLengthAsync($"scale-trash/{org.Value}"));
                output.WriteLine(
                    $"worker purge business completion ({ids.Count} tombstones + deleted objects) | {timer.Elapsed.TotalMilliseconds:0.0} | n/a"
                );
                timer.Restart();
                await bus.InvokeAsync(new MaintainAuditPartitions());
                output.WriteLine(
                    $"audit partition upkeep completed | {timer.Elapsed.TotalMilliseconds:0.0} | n/a"
                );
                await DrainAsync("after upkeep, traffic still active");
            }
        }
        finally
        {
            backgroundDone.Cancel();
            trafficErrors = await traffic;
        }
        Assert.Equal(0, trafficErrors);
        await DrainAsync("after all traffic stopped");
    }

    private async Task DrainAsync(string phase)
    {
        var timer = Stopwatch.StartNew();
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                await using var connection = new Npgsql.NpgsqlConnection(
                    fixture.PostgresConnectionString
                );
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT (SELECT count(*) FROM wolverine.wolverine_incoming_envelopes WHERE status <> 'Handled') + (SELECT count(*) FROM wolverine.wolverine_outgoing_envelopes) + (SELECT count(*) FROM wolverine.wolverine_dead_letters)";
                return Equals(0L, await command.ExecuteScalarAsync());
            },
            $"durable queues to drain without dead letters ({phase})",
            TimeSpan.FromMinutes(3)
        );
        output.WriteLine(
            $"durable queue drain ({phase}) | {timer.Elapsed.TotalMilliseconds:0.0} | n/a"
        );
    }

    private async Task<int> MeasureMixedAsync(
        HttpClient owner,
        HttpClient guest,
        CancellationToken backgroundDone
    )
    {
        var targets = new (HttpClient Client, string Path)[]
        {
            (owner, "/api/sites?limit=50"),
            (owner, "/api/sites?limit=50&offset=950"),
            (owner, "/api/listings/feed"),
            (owner, "/api/audit/changes?limit=500"),
            (guest, "/public/sites"),
            (guest, "/public/sites?near=42.36,-71.05"),
        };
        var samples = new ConcurrentBag<(string Path, double Ms, int Status)>();
        using var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var timer = Stopwatch.StartNew();
        await Task.WhenAll(
            Enumerable
                .Range(0, 16)
                .Select(async worker =>
                {
                    var next = worker;
                    // Sustain reads through every background phase, for at least a minute.
                    while (
                        timer.Elapsed < TimeSpan.FromSeconds(60)
                        || !backgroundDone.IsCancellationRequested
                    )
                    {
                        var target = targets[next++ % targets.Length];
                        var started = Stopwatch.GetTimestamp();
                        var status = 0;
                        try
                        {
                            using var response = await target.Client.GetAsync(target.Path);
                            await response.Content.ReadAsByteArrayAsync();
                            status = (int)response.StatusCode;
                        }
                        catch (HttpRequestException) { }
                        catch (TaskCanceledException) { }
                        samples.Add(
                            (
                                target.Path,
                                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                                status
                            )
                        );
                    }
                })
        );
        process.Refresh();
        output.WriteLine(
            $"mixed workload: 16 closed-loop clients, {timer.Elapsed.TotalSeconds:0.0}s, {samples.Count / timer.Elapsed.TotalSeconds:0.0} requests/s; in-process TestServer + handlers, not network/deployment capacity"
        );
        output.WriteLine(
            $"test-host CPU seconds={(process.TotalProcessorTime - cpuStart).TotalSeconds:0.0}, end-of-traffic RSS MiB={process.WorkingSet64 / 1048576.0:0.0}"
        );
        foreach (var group in samples.GroupBy(s => s.Path))
        {
            var times = group.Select(s => s.Ms).Order().ToArray();
            double Percentile(double p) => times[(int)Math.Ceiling(times.Length * p) - 1];
            output.WriteLine(
                $"{group.Key}: n={times.Length}, p50={Percentile(.5):0.0}, p95={Percentile(.95):0.0}, p99={Percentile(.99):0.0} ms; statuses={string.Join(',', group.GroupBy(s => s.Status).Select(g => $"{g.Key}:{g.Count()}"))}"
            );
        }
        return samples.Count(s => s.Status is < 200 or > 299);
    }

    private async Task ReportAsync(HttpClient client, string path, int runs = 10)
    {
        await client.GetAsync(path);
        var samples = new List<double>(runs);
        var bytes = 0L;
        for (var i = 0; i < runs; i++)
        {
            var timer = Stopwatch.StartNew();
            var response = await client.GetAsync(path);
            var body = await response.Content.ReadAsByteArrayAsync();
            timer.Stop();
            Assert.True(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode}");
            samples.Add(timer.Elapsed.TotalMilliseconds);
            bytes = body.Length;
        }
        samples.Sort();
        output.WriteLine($"{path} | {samples[samples.Count / 2]:0.0} | {bytes}");
    }

    private async Task SeedAsync(Guid root)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("org", fixture.OrgA.Value);
        command.Parameters.AddWithValue("root", root);
        command.Parameters.AddWithValue("region", RegionId.Default.Value);
        command.CommandText = """
            INSERT INTO tenancy.organizations (id, name, slug, region, status, created_at, is_platform)
            SELECT md5('scale-org-' || g)::uuid, 'Scale Org ' || g, 'scale-org-' || g,
                   @region, 'Active', now(), false
            FROM generate_series(1, 1000) AS g
            ON CONFLICT DO NOTHING;

            WITH generated AS (
                SELECT g, md5('scale-site-' || g)::uuid AS id
                FROM generate_series(1, 1000) AS g
            )
            INSERT INTO tenancy.sites
                (id, org_id, node_id, name, time_zone, path, status, city, latitude,
                 longitude, created_at, external_id, attributes)
            SELECT id, @org, @root, 'Scale Site ' || lpad(g::text, 4, '0'), 'Etc/UTC',
                   ((SELECT path::text FROM tenancy.hierarchy_nodes WHERE id = @root)
                    || '.s' || replace(id::text, '-', ''))::ltree,
                   'Open', 'Scale City', 42.36 + (g % 100) / 1000.0,
                   -71.05 - (g % 100) / 1000.0, now(), 'scale-' || g, '{}'::jsonb
            FROM generated
            ON CONFLICT DO NOTHING;

            INSERT INTO tenancy.site_schedules
                (id, org_id, site_id, name, rrule, anchor_date, opens_local,
                 closes_local, ex_dates)
            SELECT md5('scale-schedule-' || s.id)::uuid, s.org_id, s.id, 'Regular',
                   'FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR', current_date, '09:00', '17:00', '{}'
            FROM tenancy.sites AS s
            WHERE s.org_id = @org AND s.external_id LIKE 'scale-%'
            ON CONFLICT DO NOTHING;

            INSERT INTO audit.change_log
                (id, org_id, actor_tier, schema_name, table_name, row_id, operation,
                 diff, occurred_at)
            SELECT md5('scale-audit-' || g)::uuid, @org, 'system', 'tenancy', 'sites',
                   g::text, 'updated', '{}'::jsonb, now() - g * interval '1 second'
            FROM generate_series(1, 10000) AS g
            ON CONFLICT DO NOTHING;

            INSERT INTO storage.files
                (id, org_id, key, name, content_type, max_bytes, status,
                 legal_hold, created_by, created_at, deleted_at)
            SELECT md5('scale-trash-' || id)::uuid, id, 'scale-trash/' || id,
                   'Expired benchmark file', 'application/octet-stream', 1,
                   'Deleted', false, '00000000-0000-0000-0000-000000000001'::uuid,
                   now() - interval '60 days', now() - interval '40 days'
            FROM tenancy.organizations
            ON CONFLICT DO NOTHING;
            """;
        await command.ExecuteNonQueryAsync();
    }
}
