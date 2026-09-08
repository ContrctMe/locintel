using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Modules.Alerts.Data;
using LocIntel.Modules.Cases.Data;
using LocIntel.Modules.Entities.Data;
using LocIntel.Modules.Incidents.Data;
using LocIntel.Modules.Ingest.Data;
using LocIntel.Modules.Storage.Data;
using LocIntel.Modules.Tenancy.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

public class ReadTransactionTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Storage_and_ingest_reads_do_not_acquire_endpoint_transactions()
    {
        var transactions = 0;
        await using var host = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                void Observe<T>()
                    where T : DbContext =>
                    services.ConfigureDbContext<T>(options =>
                        options.LogTo(
                            _ => Interlocked.Increment(ref transactions),
                            (eventId, _) => eventId == RelationalEventId.TransactionStarted
                        )
                    );
                Observe<StorageDbContext>();
                Observe<IngestDbContext>();
            })
        );
        using var client = host.CreateDefaultClient(
            new RedirectHandler(),
            new CookieContainerHandler()
        );
        (
            await client.GetAsync(
                $"/auth/login?returnUrl=%2Fme&hint={Uri.EscapeDataString(ApiFixture.UserA)}"
            )
        ).EnsureSuccessStatusCode();
        using var created = await client.PostAsJsonAsync(
            "/api/files",
            new
            {
                name = "read-probe.txt",
                contentType = "text/plain",
                sizeBytes = 4,
            }
        );
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("fileId")
            .GetGuid();
        var missingBatch = $"/api/ingest/batches/{Guid.NewGuid()}";
        foreach (
            var path in new[]
            {
                "/api/files",
                $"/api/files/{id}",
                "/api/ingest/batches",
                "/api/connectors",
                missingBatch,
            }
        )
        {
            Interlocked.Exchange(ref transactions, 0);
            using var response = await client.GetAsync(path);
            Assert.Equal(
                path == missingBatch
                    ? System.Net.HttpStatusCode.NotFound
                    : System.Net.HttpStatusCode.OK,
                response.StatusCode
            );
            Assert.True(Volatile.Read(ref transactions) == 0, $"{path} started a transaction");
        }
    }

    [Fact]
    public async Task Incident_list_does_not_start_an_endpoint_transaction()
    {
        var transactions = 0;
        await using var host = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.ConfigureDbContext<IncidentsDbContext>(options =>
                    options.LogTo(
                        _ => Interlocked.Increment(ref transactions),
                        (eventId, _) => eventId == RelationalEventId.TransactionStarted
                    )
                )
            )
        );
        using var client = host.CreateDefaultClient(
            new RedirectHandler(),
            new CookieContainerHandler()
        );
        using var login = await client.GetAsync(
            $"/auth/login?returnUrl=%2Fme&hint={Uri.EscapeDataString(ApiFixture.UserA)}"
        );
        login.EnsureSuccessStatusCode();
        Interlocked.Exchange(ref transactions, 0);
        using var response = await client.GetAsync("/api/incidents?limit=50");
        response.EnsureSuccessStatusCode();
        Assert.Equal(0, Volatile.Read(ref transactions));
    }

    [Theory]
    [InlineData("Dynamic")]
    [InlineData("Static")]
    public async Task Reviewed_reads_release_transactions_while_writes_keep_them(string mode)
    {
        var transactions = new ConcurrentDictionary<string, int>();
        await using var host = fixture.Factory.WithWebHostBuilder(builder =>
            builder
                .UseSetting("CodeGeneration:Mode", mode)
                .ConfigureServices(services =>
                {
                    void Observe<T>()
                        where T : DbContext =>
                        services.ConfigureDbContext<T>(
                            (sp, options) =>
                            {
                                var accessor = sp.GetRequiredService<IHttpContextAccessor>();
                                options.LogTo(
                                    _ =>
                                    {
                                        if (accessor.HttpContext is { } http)
                                            transactions.AddOrUpdate(
                                                http.Request.Path.Value!,
                                                1,
                                                (_, count) => count + 1
                                            );
                                    },
                                    (eventId, _) => eventId == RelationalEventId.TransactionStarted
                                );
                            }
                        );
                    Observe<IncidentsDbContext>();
                    Observe<TenancyDbContext>();
                    Observe<EntitiesDbContext>();
                    Observe<CasesDbContext>();
                    Observe<AlertsDbContext>();
                })
        );
        using var client = host.CreateDefaultClient(
            new RedirectHandler(),
            new CookieContainerHandler()
        );
        using var login = await client.GetAsync(
            $"/auth/login?returnUrl=%2Fme&hint={Uri.EscapeDataString(ApiFixture.UserA)}"
        );
        login.EnsureSuccessStatusCode();
        var site = await ApiFixture.EnsureSiteAsync(client, "Transaction lifetime");
        using var created = await client.PostAsJsonAsync(
            "/api/incidents",
            new
            {
                siteId = site,
                title = "Transaction lifetime",
                category = "Theft",
                severity = "Low",
                occurredAt = DateTimeOffset.UtcNow,
            }
        );
        created.EnsureSuccessStatusCode();
        var incident = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        Assert.True(transactions.GetValueOrDefault("/api/incidents") > 0);
        transactions.Clear();
        foreach (
            var path in new[]
            {
                "/api/incidents",
                $"/api/incidents/{incident}",
                "/api/incidents/stats",
                "/api/sites",
                "/api/sites/open-now",
                $"/api/sites/{site}",
                $"/api/sites/{site}/schedules",
                $"/api/sites/{site}/windows",
                "/api/entities",
                "/api/cases",
                "/api/alerts",
                "/api/alerts/summary",
            }
        )
        {
            using var response = await client.GetAsync(path);
            response.EnsureSuccessStatusCode();
            Assert.True(
                transactions.GetValueOrDefault(path) == 0,
                $"{path} retained an endpoint transaction"
            );
        }
    }
}
