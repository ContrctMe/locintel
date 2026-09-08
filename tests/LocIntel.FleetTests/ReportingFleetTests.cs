using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace LocIntel.FleetTests;

public sealed class ReportingFleetTests(Fleet fleet) : IClassFixture<Fleet>
{
    [ReportingFleetFact]
    public async Task Report_quota_has_one_last_slot_shared_by_all_api_replicas()
    {
        using var alice = await fleet.LoginAsync(Fleet.Alice);
        using var op = await fleet.LoginAsync(Fleet.Operator);
        var org = (await Fleet.JsonAsync(await alice.GetAsync("/me")))
            .GetProperty("activeOrg")
            .GetGuid();
        var root = await Fleet.RootNodeAsync(alice);
        var site = await Fleet.JsonAsync(
            await alice.PostAsJsonAsync(
                "/api/sites",
                new
                {
                    name = $"Report quota {Guid.NewGuid():N}",
                    nodeId = root,
                    timeZone = "UTC",
                }
            )
        );
        var before = await Fleet.JsonAsync(await alice.GetAsync("/api/reports/quota"));
        var ceiling =
            before.GetProperty("consumed").GetInt64()
            + before.GetProperty("reserved").GetInt64()
            + 1;
        var endpoint = $"/api/operator/orgs/{org}/entitlements/reports.monthly";
        (
            await op.PutAsJsonAsync(endpoint, new { value = ceiling.ToString() })
        ).EnsureSuccessStatusCode();
        try
        {
            var responses = await Task.WhenAll(
                Enumerable
                    .Range(0, 12)
                    .Select(_ =>
                        alice.PostAsJsonAsync(
                            "/api/reports",
                            new
                            {
                                reportType = "site",
                                mode = "single",
                                selection = "selected",
                                siteIds = new[] { site.GetProperty("id").GetGuid() },
                                options = new { },
                            }
                        )
                    )
            );
            Assert.True(
                responses.Select(Fleet.InstanceOf).Distinct().Count() >= 2,
                "Quota contention must reach multiple API replicas."
            );
            var accepted = Assert.Single(responses, x => x.IsSuccessStatusCode);
            Assert.All(
                responses.Where(x => !x.IsSuccessStatusCode),
                x =>
                    Assert.Contains(
                        x.StatusCode,
                        new[]
                        {
                            System.Net.HttpStatusCode.PaymentRequired,
                            System.Net.HttpStatusCode.ServiceUnavailable,
                        }
                    )
            );
            var id = (await Fleet.JsonAsync(accepted)).GetProperty("id").GetGuid();
            await Fleet.WaitUntilAsync(
                async () =>
                    (await Fleet.JsonAsync(await alice.GetAsync($"/api/reports/{id}")))
                        .GetProperty("state")
                        .GetString() == "Completed",
                "last quota slot finishes"
            );
            var after = await Fleet.JsonAsync(await alice.GetAsync("/api/reports/quota"));
            Assert.Equal(0, after.GetProperty("remaining").GetInt64());
            Assert.Equal(
                before.GetProperty("consumed").GetInt64() + 1,
                after.GetProperty("consumed").GetInt64()
            );
        }
        finally
        {
            (
                await op.PutAsJsonAsync(
                    endpoint,
                    new { value = before.GetProperty("limit").GetInt64().ToString() }
                )
            ).EnsureSuccessStatusCode();
        }
    }

    [ReportingFleetFact]
    public async Task Reports_survive_the_generating_process_dying_and_preserve_completed_pdfs()
    {
        Assert.True(fleet.Replicas >= 2);
        using var alice = await fleet.LoginAsync(Fleet.Alice);
        using var op = await fleet.LoginAsync(Fleet.Operator);
        var org = (await Fleet.JsonAsync(await alice.GetAsync("/me")))
            .GetProperty("activeOrg")
            .GetGuid();
        (
            await op.PutAsJsonAsync(
                $"/api/operator/orgs/{org}/entitlements/sites.max",
                new { value = "100000" }
            )
        ).EnsureSuccessStatusCode();
        var root = await Fleet.RootNodeAsync(alice);
        var ids = new List<Guid>();
        for (int i = 0; i < 100; i++)
        {
            var created = await Fleet.JsonAsync(
                await alice.PostAsJsonAsync(
                    "/api/sites",
                    new
                    {
                        name = $"Report fleet {i:000} {Guid.NewGuid():N}",
                        nodeId = root,
                        timeZone = "UTC",
                    }
                )
            );
            ids.Add(created.GetProperty("id").GetGuid());
        }
        var quotaBefore = await Fleet.JsonAsync(await alice.GetAsync("/api/reports/quota"));
        var workers = (
            Environment.GetEnvironmentVariable("LOCINTEL_FLEET_WORKER_PIDS")
            ?? throw new InvalidOperationException(
                "Reporting fleet requires its owned worker PIDs."
            )
        )
            .Split(',')
            .Select(int.Parse)
            .ToArray();
        Assert.Equal(fleet.WorkerPorts.Length, workers.Length);
        using var health = new HttpClient();
        foreach (var port in fleet.WorkerPorts)
        {
            var response = await health.GetAsync($"http://127.0.0.1:{port}/healthz");
            response.EnsureSuccessStatusCode();
        }
        var logDirectory =
            Environment.GetEnvironmentVariable("LOCINTEL_FLEET_REPORT_LOG_DIR")
            ?? throw new InvalidOperationException(
                "Reporting fleet requires its owned worker logs."
            );
        var started = Stopwatch.StartNew();
        var submitted = await alice.PostAsJsonAsync(
            "/api/reports",
            new
            {
                reportType = "site",
                mode = "bulk",
                selection = "selected",
                siteIds = ids,
                options = new { },
            }
        );
        var submittingApi = Fleet.InstanceOf(submitted);
        var jobId = (await Fleet.JsonAsync(submitted)).GetProperty("id").GetGuid();
        JsonElement job = default;
        await Fleet.WaitUntilAsync(
            async () =>
            {
                job = await Fleet.JsonAsync(await alice.GetAsync($"/api/reports/{jobId}"));
                return job.GetProperty("state").GetString() == "Running"
                    && job.GetProperty("artifacts").GetArrayLength() >= 1
                    && job.GetProperty("artifacts")[0].GetProperty("fileId").ValueKind
                        == JsonValueKind.String;
            },
            "report has a completed PDF before process death"
        );
        var originalArtifact = job.GetProperty("artifacts")[0];
        var artifactId = originalArtifact.GetProperty("id").GetGuid();
        var original = SHA256.HashData(await Download(originalArtifact));
        // Match the actual claim to a PID started and recorded by this harness.
        // The submitting API is only a publisher and must remain alive.
        var claimLogs = string.Join(
            "\n",
            Directory.GetFiles(logDirectory, "worker-*.log").Select(File.ReadAllText)
        );
        var pid = Assert.Single(
            workers,
            worker =>
                claimLogs
                    .Split('\n')
                    .Any(line => line.Trim() == $"Report {jobId} claimed by process {worker}")
        );
        Assert.NotEqual(int.Parse(submittingApi[(submittingApi.LastIndexOf(':') + 1)..]), pid);
        var instance = $"worker:{pid}";
        Process.GetProcessById(pid).Kill();
        await Fleet.WaitUntilAsync(
            async () =>
            {
                var response = await alice.GetAsync($"/api/reports/{jobId}");
                if (!response.IsSuccessStatusCode)
                    return false;
                job = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.DoesNotContain(
                    job.GetProperty("state").GetString(),
                    new[] { "Failed", "CompletedWithErrors" }
                );
                return job.GetProperty("state").GetString() == "Completed"
                    && job.GetProperty("artifacts")
                        .EnumerateArray()
                        .Where(x => x.GetProperty("contentType").GetString() == "application/pdf")
                        .All(x => x.GetProperty("fileId").ValueKind == JsonValueKind.String);
            },
            "100 reports recover after process death",
            TimeSpan.FromMinutes(5)
        );
        Assert.Equal(
            original,
            SHA256.HashData(
                await Download(
                    job.GetProperty("artifacts")
                        .EnumerateArray()
                        .Single(x => x.GetProperty("id").GetGuid() == artifactId)
                )
            )
        );
        var quotaAfter = await Fleet.JsonAsync(await alice.GetAsync("/api/reports/quota"));
        Assert.Equal(
            quotaBefore.GetProperty("consumed").GetInt64() + 100,
            quotaAfter.GetProperty("consumed").GetInt64()
        );
        Assert.Equal(
            quotaBefore.GetProperty("reserved").GetInt64(),
            quotaAfter.GetProperty("reserved").GetInt64()
        );
        var apiInstances = new HashSet<string>();
        for (var i = 0; i < fleet.Replicas * 4; i++)
        {
            var response = await alice.GetAsync("/me");
            response.EnsureSuccessStatusCode();
            apiInstances.Add(Fleet.InstanceOf(response));
        }
        Assert.Equal(fleet.Replicas, apiInstances.Count);
        Assert.Equal(100, job.GetProperty("items").GetArrayLength());
        var artifacts = job.GetProperty("artifacts").EnumerateArray().ToArray();
        Assert.Equal(101, artifacts.Length);
        var fileList = await Fleet.JsonAsync(await alice.GetAsync("/api/files?limit=200"));
        var siteFiles = fileList
            .GetProperty("items")
            .EnumerateArray()
            .Where(x =>
                x.GetProperty("originId").ValueKind == JsonValueKind.String
                && x.GetProperty("originId").GetGuid() == jobId
            )
            .ToArray();
        Assert.Equal(100, siteFiles.Length);
        Assert.All(
            siteFiles,
            file =>
            {
                Assert.Single(file.GetProperty("siteIds").EnumerateArray());
                Assert.Contains(
                    artifacts,
                    artifact =>
                        artifact.GetProperty("fileId").ValueKind == JsonValueKind.String
                        && artifact.GetProperty("fileId").GetGuid()
                            == file.GetProperty("id").GetGuid()
                );
            }
        );
        var bytes = await Download(
            artifacts.Single(a => a.GetProperty("contentType").GetString() == "application/zip")
        );
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Equal(101, zip.Entries.Count);
        using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
        Assert.All(
            manifest.RootElement.GetProperty("items").EnumerateArray(),
            i => Assert.Equal("Succeeded", i.GetProperty("State").GetString())
        );
        var output = Path.Combine(Path.GetTempPath(), "locintel-reporting-fleet");
        Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(Path.Combine(output, "recovered-100.zip"), bytes);
        await File.WriteAllTextAsync(
            Path.Combine(output, "metrics.json"),
            JsonSerializer.Serialize(
                new
                {
                    replicas = fleet.Replicas,
                    workers = fleet.WorkerPorts.Length,
                    killedInstance = instance,
                    elapsedMilliseconds = started.ElapsedMilliseconds,
                    pdfCount = 100,
                    zipBytes = bytes.Length,
                }
            )
        );

        async Task<byte[]> Download(JsonElement artifact)
        {
            var path =
                artifact.GetProperty("contentType").GetString() == "application/pdf"
                    ? $"/api/files/{artifact.GetProperty("fileId").GetGuid()}/download"
                    : $"/api/reports/{jobId}/artifacts/{artifact.GetProperty("id").GetGuid()}/download";
            var link = await Fleet.JsonAsync(await alice.GetAsync(path));
            return await alice.GetByteArrayAsync(link.GetProperty("url").GetString());
        }
    }
}
