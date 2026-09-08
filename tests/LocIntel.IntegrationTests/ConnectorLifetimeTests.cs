using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Modules.Ingest;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace LocIntel.IntegrationTests;

public class ConnectorLifetimeTests(ScanLifetimeFixture fixture)
    : IClassFixture<ScanLifetimeFixture>
{
    [Fact]
    public async Task Remote_fetch_releases_connections_and_stages_a_batch()
    {
        using var client = await fixture.LoginAsync(ApiFixture.UserA);
        await using var remote = WebApplication.CreateSlimBuilder().Build();
        remote.Urls.Add("http://127.0.0.1:0");
        var observed = 0;
        remote.MapGet(
            "/sites",
            () =>
            {
                observed++;
                Assert.True(fixture.Probe.ConnectionsObserved > 0);
                Assert.Empty(fixture.Probe.OpenConnections);
                return Results.Json(Array.Empty<object>());
            }
        );
        await remote.StartAsync();
        var name = "lifetime-" + Guid.NewGuid().ToString("N");
        using var created = await client.PostAsJsonAsync(
            "/api/connectors",
            new
            {
                name,
                url = remote.Urls.Single() + "/sites",
                apiKey = "test-key",
            }
        );
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        using var scope = fixture.Factory.Services.CreateScope();
        var message = new SyncSiteConnector(id);
        fixture.Probe.FailBatchCommitFor = message.BatchId;
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope
                    .ServiceProvider.GetRequiredService<IMessageBus>()
                    .InvokeForTenantAsync(fixture.OrgA.Value.ToString(), message)
            );
            Assert.Equal("injected batch commit failure", error.Message);
            Assert.Null(await fixture.QueryIngestBatch(name));
            var connectors = await client.GetFromJsonAsync<JsonElement>("/api/connectors");
            Assert.Equal(
                JsonValueKind.Null,
                connectors
                    .EnumerateArray()
                    .Single(c => c.GetProperty("id").GetGuid() == id)
                    .GetProperty("lastSyncedAt")
                    .ValueKind
            );
        }
        finally
        {
            fixture.Probe.FailBatchCommitFor = null;
        }
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeForTenantAsync(fixture.OrgA.Value.ToString(), message);
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeForTenantAsync(fixture.OrgA.Value.ToString(), message);
        Assert.Equal(2, observed); // failed attempt, successful retry; committed replay does no I/O
        Assert.NotNull(await fixture.QueryIngestBatch(name));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("failure")]
    public async Task Changed_or_failed_source_does_not_stage_stale_data_but_keeps_access_audit(
        string action
    )
    {
        using var client = await fixture.LoginAsync(ApiFixture.UserA);
        await using var remote = WebApplication.CreateSlimBuilder().Build();
        remote.Urls.Add("http://127.0.0.1:0");
        var id = Guid.Empty;
        var name = "stale-" + Guid.NewGuid().ToString("N");
        remote.MapGet(
            "/sites",
            async () =>
            {
                Assert.Empty(fixture.Probe.OpenConnections);
                if (action == "update")
                    (
                        await client.PutAsJsonAsync(
                            $"/api/connectors/{id}",
                            new
                            {
                                name = name + "-changed",
                                url = remote.Urls.Single() + "/sites",
                                apiKey = "rotated",
                            }
                        )
                    ).EnsureSuccessStatusCode();
                if (action == "delete")
                    (await client.DeleteAsync($"/api/connectors/{id}")).EnsureSuccessStatusCode();
                return action == "failure"
                    ? Results.StatusCode(503)
                    : Results.Json(Array.Empty<object>());
            }
        );
        await remote.StartAsync();
        using var created = await client.PostAsJsonAsync(
            "/api/connectors",
            new
            {
                name,
                url = remote.Urls.Single() + "/sites",
                apiKey = "test-key",
            }
        );
        created.EnsureSuccessStatusCode();
        id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var scope = fixture.Factory.Services.CreateScope();
        var run = scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeForTenantAsync(fixture.OrgA.Value.ToString(), new SyncSiteConnector(id));
        if (action == "failure")
            await Assert.ThrowsAsync<HttpRequestException>(() => run);
        else
            await run;
        Assert.Null(await fixture.QueryIngestBatch(name));
        Assert.Null(await fixture.QueryIngestBatch(name + "-changed"));
        Assert.Contains(
            await fixture.QueryAudit(db =>
                db.DomainEvents.Where(e => e.EventName == "connector.credentials_accessed")
            ),
            e => e.Payload.Contains(id.ToString())
        );
    }
}
