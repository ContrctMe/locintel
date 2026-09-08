using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Modules.Storage;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine;

namespace LocIntel.IntegrationTests;

public class ScanLifetimeTests(ScanLifetimeFixture fixture) : IClassFixture<ScanLifetimeFixture>
{
    [Fact]
    public async Task Scanning_releases_its_database_connection_and_commits_the_result()
    {
        using var client = await fixture.LoginAsync(ApiFixture.UserA);
        var id = await Prepare(client);
        using var scope = fixture.Factory.Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeForTenantAsync(fixture.OrgA.Value.ToString(), new ScanUploadedFile(id));
        var file = await client.GetFromJsonAsync<JsonElement>($"/api/files/{id}");
        Assert.Equal("Clean", file.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Erasure_during_scanning_is_not_overwritten_by_a_late_clean_verdict()
    {
        using var client = await fixture.LoginAsync(ApiFixture.UserA);
        var id = await Prepare(client);
        fixture.Probe.DuringScan = async () =>
            (await client.DeleteAsync($"/api/files/{id}")).EnsureSuccessStatusCode();
        try
        {
            using var scope = fixture.Factory.Services.CreateScope();
            await scope
                .ServiceProvider.GetRequiredService<IMessageBus>()
                .InvokeForTenantAsync(fixture.OrgA.Value.ToString(), new ScanUploadedFile(id));
            Assert.Equal(
                System.Net.HttpStatusCode.NotFound,
                (await client.GetAsync($"/api/files/{id}/download")).StatusCode
            );
        }
        finally
        {
            fixture.Probe.DuringScan = null;
        }
    }

    private async Task<Guid> Prepare(HttpClient client, bool cleanText = false)
    {
        using var created = await client.PostAsJsonAsync(
            "/api/files",
            new
            {
                name = "scan-lifetime.bin",
                contentType = cleanText ? "text/plain" : "application/octet-stream",
                sizeBytes = 4,
            }
        );
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("fileId")
            .GetGuid();
        await fixture
            .Factory.Services.GetRequiredService<IObjectStore>()
            .WriteAsync(
                $"{RegionId.Default.Value}/{fixture.OrgA.Value}/files/{id}",
                new MemoryStream(new byte[4]),
                "application/octet-stream"
            );
        await using var connection = new NpgsqlConnection(fixture.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE storage.files SET status = @status, scanned_at = CASE WHEN @status = 'Clean' THEN now() ELSE NULL END WHERE id = @id",
            connection
        );
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("status", cleanText ? "Clean" : "Uploaded");
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return id;
    }

    [Fact]
    public async Task Preview_source_read_releases_connection_and_persists_preview()
    {
        using var client = await fixture.LoginAsync(ApiFixture.UserA);
        var id = await Prepare(client, cleanText: true);
        fixture.Probe.BeforeRead = () =>
        {
            Assert.True(fixture.Probe.ConnectionsObserved > 0);
            Assert.Empty(fixture.Probe.OpenConnections);
            return Task.CompletedTask;
        };
        try
        {
            using var scope = fixture.Factory.Services.CreateScope();
            await scope
                .ServiceProvider.GetRequiredService<IMessageBus>()
                .InvokeForTenantAsync(fixture.OrgA.Value.ToString(), new GenerateDerivatives(id));
            Assert.True(
                (await client.GetFromJsonAsync<JsonElement>($"/api/files/{id}"))
                    .GetProperty("hasPreview")
                    .GetBoolean()
            );
        }
        finally
        {
            fixture.Probe.BeforeRead = null;
        }
    }

    [Fact]
    public async Task Purge_erases_preview_bytes_even_when_the_preview_commit_failed()
    {
        using var client = await fixture.LoginAsync(ApiFixture.UserA);
        var id = await Prepare(client, cleanText: true);
        var key = $"{RegionId.Default.Value}/{fixture.OrgA.Value}/files/{id}";
        fixture.Probe.FailCommitFor = id;
        try
        {
            using var scope = fixture.Factory.Services.CreateScope();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope
                    .ServiceProvider.GetRequiredService<IMessageBus>()
                    .InvokeForTenantAsync(
                        fixture.OrgA.Value.ToString(),
                        new SaveFilePreview(id, key, new byte[4])
                    )
            );
        }
        finally
        {
            fixture.Probe.FailCommitFor = null;
        }
        var store = fixture.Factory.Services.GetRequiredService<IObjectStore>();
        Assert.Equal(4, await store.GetLengthAsync(key + ".preview.txt"));
        Assert.False(
            (await client.GetFromJsonAsync<JsonElement>($"/api/files/{id}"))
                .GetProperty("hasPreview")
                .GetBoolean()
        );
        (await client.DeleteAsync($"/api/files/{id}")).EnsureSuccessStatusCode();
        await using var connection = new NpgsqlConnection(fixture.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE storage.files SET deleted_at = now() - interval '31 days' WHERE id = @id",
            connection
        );
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
        using var purge = fixture.Factory.Services.CreateScope();
        await purge
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeForTenantAsync(fixture.OrgA.Value.ToString(), new PurgeFileTrash());
        Assert.Null(await store.GetLengthAsync(key + ".preview.txt"));
    }

    [Fact]
    public async Task Deletion_during_preview_read_prevents_preview_publication()
    {
        using var client = await fixture.LoginAsync(ApiFixture.UserA);
        var id = await Prepare(client, cleanText: true);
        fixture.Probe.BeforeRead = async () =>
            (await client.DeleteAsync($"/api/files/{id}")).EnsureSuccessStatusCode();
        try
        {
            using var scope = fixture.Factory.Services.CreateScope();
            await scope
                .ServiceProvider.GetRequiredService<IMessageBus>()
                .InvokeForTenantAsync(fixture.OrgA.Value.ToString(), new GenerateDerivatives(id));
            Assert.Null(
                await fixture
                    .Factory.Services.GetRequiredService<IObjectStore>()
                    .GetLengthAsync(
                        $"{RegionId.Default.Value}/{fixture.OrgA.Value}/files/{id}.preview.txt"
                    )
            );
        }
        finally
        {
            fixture.Probe.BeforeRead = null;
        }
    }

    [Fact]
    public async Task Org_purge_waits_for_inflight_preview_and_erases_its_bytes()
    {
        using var client = await fixture.LoginAsync(ApiFixture.UserA);
        var id = await Prepare(client, cleanText: true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Probe.BeforePreviewWrite = async () =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
        };
        using var scope = fixture.Factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var generation = bus.InvokeForTenantAsync(
            fixture.OrgA.Value.ToString(),
            new GenerateDerivatives(id)
        );
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Purge only this test's tenant; each test class has an isolated database.
            var purge = bus.InvokeForTenantAsync(
                fixture.OrgA.Value.ToString(),
                new LocIntel.Contracts.PurgeOrgFiles()
            );
            Assert.NotSame(purge, await Task.WhenAny(purge, Task.Delay(150)));
            release.TrySetResult();
            await Task.WhenAll(generation, purge);
            Assert.Null(
                await fixture
                    .Factory.Services.GetRequiredService<IObjectStore>()
                    .GetLengthAsync(
                        $"{RegionId.Default.Value}/{fixture.OrgA.Value}/files/{id}.preview.txt"
                    )
            );
        }
        finally
        {
            release.TrySetResult();
            fixture.Probe.BeforePreviewWrite = null;
        }
    }

    [Fact]
    public async Task Concurrent_and_replayed_scan_results_emit_one_quarantine_audit()
    {
        using var client = await fixture.LoginAsync(ApiFixture.UserA);
        var id = await Prepare(client);
        var message = new ApplyFileScan(
            id,
            $"{RegionId.Default.Value}/{fixture.OrgA.Value}/files/{id}",
            ScanVerdict.Infected
        );
        async Task Apply(string tenant)
        {
            using var scope = fixture.Factory.Services.CreateScope();
            await scope
                .ServiceProvider.GetRequiredService<IMessageBus>()
                .InvokeForTenantAsync(tenant, message);
        }
        await Apply(fixture.OrgB.Value.ToString());
        Assert.Equal(
            "Uploaded",
            (await client.GetFromJsonAsync<JsonElement>($"/api/files/{id}"))
                .GetProperty("status")
                .GetString()
        );
        await Task.WhenAll(
            Apply(fixture.OrgA.Value.ToString()),
            Apply(fixture.OrgA.Value.ToString())
        );
        await Apply(fixture.OrgA.Value.ToString());
        Assert.Equal(
            "Quarantined",
            (await client.GetFromJsonAsync<JsonElement>($"/api/files/{id}"))
                .GetProperty("status")
                .GetString()
        );
        await ApiFixture.WaitUntilAsync(
            async () =>
                (
                    await fixture.QueryAudit(db =>
                        db.DomainEvents.Where(e => e.EventName == "file.quarantined")
                    )
                ).Count(e => e.Payload.Contains(id.ToString())) == 1,
            "one committed quarantine audit"
        );
    }

    [Fact]
    public async Task Cancelled_scan_remains_unavailable_and_can_be_retried()
    {
        using var client = await fixture.LoginAsync(ApiFixture.UserA);
        var id = await Prepare(client);
        fixture.Probe.DuringScan = () => throw new OperationCanceledException("scanner cancelled");
        using var scope = fixture.Factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                bus.InvokeForTenantAsync(fixture.OrgA.Value.ToString(), new ScanUploadedFile(id))
            );
            Assert.Equal(
                "Uploaded",
                (await client.GetFromJsonAsync<JsonElement>($"/api/files/{id}"))
                    .GetProperty("status")
                    .GetString()
            );
            Assert.Equal(
                System.Net.HttpStatusCode.NotFound,
                (await client.GetAsync($"/api/files/{id}/download")).StatusCode
            );
        }
        finally
        {
            fixture.Probe.DuringScan = null;
        }
        await bus.InvokeForTenantAsync(fixture.OrgA.Value.ToString(), new ScanUploadedFile(id));
        Assert.Equal(
            "Clean",
            (await client.GetFromJsonAsync<JsonElement>($"/api/files/{id}"))
                .GetProperty("status")
                .GetString()
        );
    }

    [Fact]
    public async Task Failed_completion_commit_rolls_back_state_and_quarantine_audit()
    {
        using var client = await fixture.LoginAsync(ApiFixture.UserA);
        var id = await Prepare(client);
        fixture.Probe.FailCommitFor = id;
        try
        {
            using var scope = fixture.Factory.Services.CreateScope();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope
                    .ServiceProvider.GetRequiredService<IMessageBus>()
                    .InvokeForTenantAsync(
                        fixture.OrgA.Value.ToString(),
                        new ApplyFileScan(
                            id,
                            $"{RegionId.Default.Value}/{fixture.OrgA.Value}/files/{id}",
                            ScanVerdict.Infected
                        )
                    )
            );
            Assert.Equal("injected scan commit failure", error.Message);
        }
        finally
        {
            fixture.Probe.FailCommitFor = null;
        }
        Assert.Equal(
            "Uploaded",
            (await client.GetFromJsonAsync<JsonElement>($"/api/files/{id}"))
                .GetProperty("status")
                .GetString()
        );
        // Retry through the public pipeline; no quarantine event from the failed
        // transaction may escape, even though it was queued before commit failed.
        using var retryScope = fixture.Factory.Services.CreateScope();
        await retryScope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeForTenantAsync(fixture.OrgA.Value.ToString(), new ScanUploadedFile(id));
        Assert.Equal(
            "Clean",
            (await client.GetFromJsonAsync<JsonElement>($"/api/files/{id}"))
                .GetProperty("status")
                .GetString()
        );
        Assert.DoesNotContain(
            await fixture.QueryAudit(db =>
                db.DomainEvents.Where(e => e.EventName == "file.quarantined")
            ),
            e => e.Payload.Contains(id.ToString())
        );
    }
}
