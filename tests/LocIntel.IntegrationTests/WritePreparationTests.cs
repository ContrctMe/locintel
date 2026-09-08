using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LocIntel.Modules.Incidents.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

public class WritePreparationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Incident_body_is_parsed_before_a_transaction_is_started()
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
        (
            await client.GetAsync(
                $"/auth/login?returnUrl=%2Fme&hint={Uri.EscapeDataString(ApiFixture.UserA)}"
            )
        ).EnsureSuccessStatusCode();
        transactions = 0;
        using var response = await client.PostAsync(
            "/api/incidents",
            new StringContent("{", Encoding.UTF8, "application/json")
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, Volatile.Read(ref transactions));
    }

    [Fact]
    public async Task Incident_commit_failure_rolls_back_the_incident_and_its_audit()
    {
        var probe = new FailIncidentCommit();
        await using var host = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.ConfigureDbContext<IncidentsDbContext>(options =>
                    options.AddInterceptors(probe)
                )
            )
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
        var siteId = await ApiFixture.EnsureSiteAsync(client, "write-preparation");
        var title = "atomic-" + Guid.NewGuid().ToString("N");
        var request = new
        {
            siteId,
            title,
            category = "Theft",
            severity = "Low",
            occurredAt = DateTimeOffset.UtcNow,
        };
        probe.Fail = true;
        using var failed = await client.PostAsJsonAsync("/api/incidents", request);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.NotEqual(Guid.Empty, probe.FailedId);
        Assert.DoesNotContain(
            (await ApiFixture.GetItemsAsync(client, "/api/incidents?q=" + title)).EnumerateArray(),
            row => row.GetProperty("title").GetString() == title
        );
        probe.Fail = false;
        using var retried = await client.PostAsJsonAsync("/api/incidents", request);
        retried.EnsureSuccessStatusCode();
        var id = (await retried.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        await ApiFixture.WaitUntilAsync(
            async () =>
                (
                    await fixture.QueryAudit(db =>
                        db.DomainEvents.Where(e => e.EventName == "incident.reported")
                    )
                ).Any(e => e.Payload.Contains(id.ToString())),
            "successful incident audit"
        );
        Assert.DoesNotContain(
            await fixture.QueryAudit(db =>
                db.DomainEvents.Where(e => e.EventName == "incident.reported")
            ),
            e => e.Payload.Contains(probe.FailedId.ToString())
        );
    }

    private class FailIncidentCommit : DbTransactionInterceptor
    {
        public bool Fail { get; set; }
        public Guid FailedId { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Fail
                && eventData.Context is IncidentsDbContext db
                && db.Incidents.Local.FirstOrDefault() is { } incident
            )
            {
                FailedId = incident.Id;
                throw new InvalidOperationException("injected incident commit failure");
            }
            return ValueTask.FromResult(result);
        }
    }
}
