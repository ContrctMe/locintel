using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Holiday closures (ADR 27's EXDATE with a product): site-level dates that
/// carve windows out of every schedule and surface on the public page.
/// </summary>
public class SiteClosureTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private async Task<(HttpClient client, Guid siteId)> Setup()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserA);
        var tree = await client.GetAsync("/api/hierarchy");
        Guid rootId;
        if (tree.StatusCode == HttpStatusCode.OK)
            rootId = (await tree.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("nodes")
                .EnumerateArray()
                .First(n => n.GetProperty("depth").GetInt32() == 0)
                .GetProperty("id")
                .GetGuid();
        else
        {
            var created = await client.PostAsJsonAsync(
                "/api/hierarchy",
                new { name = "Org A", levels = new[] { "Region" } }
            );
            created.EnsureSuccessStatusCode();
            rootId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("rootNodeId")
                .GetGuid();
        }
        var site = await client.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Closable",
                timeZone = "Etc/UTC",
            }
        );
        site.EnsureSuccessStatusCode();
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var schedule = await client.PostAsJsonAsync(
            $"/api/sites/{siteId}/schedules",
            new
            {
                name = "Daily",
                rRule = "FREQ=DAILY",
                anchorDate = DateOnly
                    .FromDateTime(DateTime.UtcNow.AddDays(-7))
                    .ToString("yyyy-MM-dd"),
                opens = "09:00",
                closes = "17:00",
            }
        );
        schedule.EnsureSuccessStatusCode();
        return (client, siteId);
    }

    private async Task<bool> HasWindowOn(HttpClient client, Guid siteId, string localDate)
    {
        var windows = await client.GetFromJsonAsync<JsonElement>(
            $"/api/sites/{siteId}/windows?days=7"
        );
        return windows
            .EnumerateArray()
            .Any(w => w.GetProperty("localDate").GetString() == localDate);
    }

    [Fact]
    public async Task Closure_carves_the_day_out_everywhere_and_removal_restores_it()
    {
        var (client, siteId) = await Setup();
        var holiday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)).ToString("yyyy-MM-dd");

        // the projection has the day before the closure
        for (var i = 0; i < 100 && !await HasWindowOn(client, siteId, holiday); i++)
            await Task.Delay(100);
        Assert.True(await HasWindowOn(client, siteId, holiday), "window never projected");

        // close the day
        (
            await client.PostAsJsonAsync($"/api/sites/{siteId}/closures", new { date = holiday })
        ).EnsureSuccessStatusCode();
        for (var i = 0; i < 100 && await HasWindowOn(client, siteId, holiday); i++)
            await Task.Delay(100);
        Assert.False(await HasWindowOn(client, siteId, holiday), "closure never carved the window");

        // it lists, and the public page announces it
        var closures = await client.GetFromJsonAsync<JsonElement>($"/api/sites/{siteId}/closures");
        Assert.Contains(closures.EnumerateArray(), c => c.GetString() == holiday);
        var guest = fixture.GuestClient();
        guest.DefaultRequestHeaders.Add("X-Forwarded-Host", "org-a.locintel.test");
        var publicSite = await guest.GetFromJsonAsync<JsonElement>($"/public/sites/{siteId}");
        Assert.Contains(
            publicSite.GetProperty("closures").EnumerateArray(),
            c => c.GetString() == holiday
        );

        // reopening the day restores the window
        (
            await client.DeleteAsync($"/api/sites/{siteId}/closures/{holiday}")
        ).EnsureSuccessStatusCode();
        for (var i = 0; i < 100 && !await HasWindowOn(client, siteId, holiday); i++)
            await Task.Delay(100);
        Assert.True(await HasWindowOn(client, siteId, holiday), "window never came back");
    }

    [Fact]
    public async Task Closures_refuse_the_past_and_sites_without_hours()
    {
        var (client, siteId) = await Setup();
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await client.PostAsJsonAsync(
                    $"/api/sites/{siteId}/closures",
                    new
                    {
                        date = DateOnly
                            .FromDateTime(DateTime.UtcNow.AddDays(-3))
                            .ToString("yyyy-MM-dd"),
                    }
                )
            ).StatusCode
        );

        // a fresh site with no schedules has nothing to close
        var tree = await client.GetFromJsonAsync<JsonElement>("/api/hierarchy");
        var rootId = tree.GetProperty("nodes")
            .EnumerateArray()
            .First(n => n.GetProperty("depth").GetInt32() == 0)
            .GetProperty("id")
            .GetGuid();
        var bare = await client.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Hourless",
                timeZone = "Etc/UTC",
            }
        );
        bare.EnsureSuccessStatusCode();
        var bareId = (await bare.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await client.PostAsJsonAsync(
                    $"/api/sites/{bareId}/closures",
                    new
                    {
                        date = DateOnly
                            .FromDateTime(DateTime.UtcNow.AddDays(5))
                            .ToString("yyyy-MM-dd"),
                    }
                )
            ).StatusCode
        );
    }
}
