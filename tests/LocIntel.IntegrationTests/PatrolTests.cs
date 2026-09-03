using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Guard rounds: routes with geofenced checkpoints, schedules that expand
/// in the site's zone, runs stamped with the site's business date, and a
/// daily activity report that joins runs with the day's incidents.
/// </summary>
public class PatrolTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Route_schedule_run_and_daily_report()
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
                name = "Patrol Store",
                timeZone = "Pacific/Auckland",
                latitude = -36.8485,
                longitude = 174.7633,
            }
        );
        site.EnsureSuccessStatusCode();
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var route = await owner.PostAsJsonAsync(
            "/api/patrols/routes",
            new
            {
                siteId,
                name = "Closing round",
                checkpoints = new object[]
                {
                    new
                    {
                        code = "DOCK",
                        label = "Loading dock",
                        latitude = -36.8486,
                        longitude = 174.7634,
                    },
                    new { code = "SAFE", label = "Cash office" },
                },
                expectedMinutes = 20,
            }
        );
        Assert.True(route.IsSuccessStatusCode, await route.Content.ReadAsStringAsync());
        var routeId = (await route.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        // every day at 22:00 site-local, anchored a week back
        var schedule = await owner.PostAsJsonAsync(
            $"/api/patrols/routes/{routeId}/schedules",
            new
            {
                rRule = "FREQ=DAILY",
                anchorDate = DateOnly
                    .FromDateTime(DateTime.UtcNow.AddDays(-7))
                    .ToString("yyyy-MM-dd"),
                startLocal = "22:00",
            }
        );
        Assert.True(schedule.IsSuccessStatusCode, await schedule.Content.ReadAsStringAsync());

        var today = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/patrols/today?siteId={siteId}"
        );
        var aucklandToday = DateOnly.FromDateTime(
            TimeZoneInfo
                .ConvertTime(
                    DateTimeOffset.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland")
                )
                .DateTime
        );
        Assert.Equal(
            aucklandToday.ToString("yyyy-MM-dd"),
            today.GetProperty("businessDate").GetString()
        );
        var expected = Assert.Single(today.GetProperty("expected").EnumerateArray());
        Assert.Equal("22:00:00", expected.GetProperty("startLocal").GetString());
        Assert.Equal(JsonValueKind.Null, expected.GetProperty("patrolId").ValueKind);

        // run it: start against the expected occurrence, scan both, end
        var started = await owner.PostAsJsonAsync(
            "/api/patrols",
            new { routeId, scheduledStartLocal = "22:00" }
        );
        Assert.True(started.IsSuccessStatusCode, await started.Content.ReadAsStringAsync());
        var patrolId = (await started.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await owner.PostAsJsonAsync($"/api/patrols/{patrolId}/scan", new { code = "NOPE" })
            ).StatusCode
        );
        var dock = await owner.PostAsJsonAsync(
            $"/api/patrols/{patrolId}/scan",
            new
            {
                code = "dock",
                latitude = -36.8487,
                longitude = 174.7635,
                note = "Clear",
            }
        );
        dock.EnsureSuccessStatusCode();
        var dockScan = await dock.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(dockScan.GetProperty("withinGeofence").GetBoolean());
        Assert.Equal(1, dockScan.GetProperty("scanned").GetInt32());
        var safe = await owner.PostAsJsonAsync(
            $"/api/patrols/{patrolId}/scan",
            new { code = "SAFE" }
        );
        Assert.Equal(
            JsonValueKind.Null,
            (await safe.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("withinGeofence")
                .ValueKind
        );
        (
            await owner.PostAsJsonAsync(
                $"/api/patrols/{patrolId}/end",
                new { summary = "All secure." }
            )
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await owner.PostAsJsonAsync($"/api/patrols/{patrolId}/scan", new { code = "DOCK" })
            ).StatusCode
        );

        today = await owner.GetFromJsonAsync<JsonElement>($"/api/patrols/today?siteId={siteId}");
        expected = Assert.Single(today.GetProperty("expected").EnumerateArray());
        Assert.Equal(patrolId, expected.GetProperty("patrolId").GetGuid());
        Assert.Equal("Completed", expected.GetProperty("status").GetString());

        // the day's incident shows up on the report, and nothing was missed
        (
            await owner.PostAsJsonAsync(
                "/api/incidents",
                new
                {
                    siteId,
                    category = "Trespass",
                    severity = "Low",
                    title = "Loiterer at dock",
                    occurredAt = DateTimeOffset.UtcNow,
                }
            )
        ).EnsureSuccessStatusCode();
        var report = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/patrols/report?siteId={siteId}&date={aucklandToday:yyyy-MM-dd}"
        );
        Assert.Equal(1, report.GetProperty("expected").GetInt32());
        Assert.Equal(1, report.GetProperty("completed").GetInt32());
        Assert.Empty(report.GetProperty("missed").EnumerateArray());
        var run = Assert.Single(report.GetProperty("patrols").EnumerateArray());
        Assert.Equal(2, run.GetProperty("checkpointsScanned").GetInt32());
        Assert.Equal(ApiFixture.UserA, run.GetProperty("startedByLabel").GetString());
        Assert.Contains(
            report.GetProperty("incidents").EnumerateArray(),
            i => i.GetProperty("title").GetString() == "Loiterer at dock"
        );

        // gates: no role holds nothing; another org sees nothing; guests nothing
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.GetAsync($"/api/patrols/today?siteId={siteId}")).StatusCode
        );
        var outsider = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.GetAsync($"/api/patrols/{patrolId}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.GetAsync($"/api/patrols/today?siteId={siteId}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.PostAsJsonAsync("/api/patrols", new { routeId })).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fixture.GuestClient().GetAsync("/api/patrols/routes")).StatusCode
        );

        // archive the route: no longer expected tomorrow's list, existing runs stay
        (
            await owner.PutAsJsonAsync(
                $"/api/patrols/routes/{routeId}",
                new
                {
                    name = "Closing round",
                    checkpoints = new object[] { new { code = "DOCK", label = "Dock" } },
                    archived = true,
                }
            )
        ).EnsureSuccessStatusCode();
        today = await owner.GetFromJsonAsync<JsonElement>($"/api/patrols/today?siteId={siteId}");
        Assert.Empty(today.GetProperty("expected").EnumerateArray());
        Assert.Single(today.GetProperty("patrols").EnumerateArray());
    }
}
