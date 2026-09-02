using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The reference vertical slice (ADR 45): scaffolded by new-module.py, site
/// info via ISiteDirectory only, the day is the SITE's business date, and
/// the three gates hold like everywhere else.
/// </summary>
public class ChecklistTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Daily_checklist_round_trip_on_the_sites_clock()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var hierarchy = await owner.PostAsJsonAsync(
            "/api/hierarchy",
            new { name = "Org A", levels = new[] { "Region" } }
        );
        hierarchy.EnsureSuccessStatusCode();
        var rootId = (await hierarchy.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("rootNodeId")
            .GetGuid();
        var created = await owner.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Checked Site",
                timeZone = "Pacific/Auckland",
            }
        );
        created.EnsureSuccessStatusCode();
        var siteId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var template = await owner.PostAsJsonAsync(
            "/api/checklists/templates",
            new { name = "Opening", items = new[] { "Unlock doors", "Count register" } }
        );
        Assert.True(template.IsSuccessStatusCode, await template.Content.ReadAsStringAsync());
        var templateId = (await template.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var today = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/checklists/today?siteId={siteId}"
        );
        // the business date is AUCKLAND's today, not UTC's (ADR 26 kind 3)
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
        var list = today.GetProperty("lists").EnumerateArray().Single();
        Assert.All(
            list.GetProperty("items").EnumerateArray(),
            i => Assert.False(i.GetProperty("done").GetBoolean())
        );

        // tick one, see it stick; untick, see it clear
        (
            await owner.PostAsJsonAsync(
                "/api/checklists/check",
                new
                {
                    templateId,
                    siteId,
                    itemIndex = 0,
                    done = true,
                }
            )
        ).EnsureSuccessStatusCode();
        today = await owner.GetFromJsonAsync<JsonElement>($"/api/checklists/today?siteId={siteId}");
        var items = today.GetProperty("lists")[0].GetProperty("items");
        Assert.True(items[0].GetProperty("done").GetBoolean());
        Assert.False(items[1].GetProperty("done").GetBoolean());
        (
            await owner.PostAsJsonAsync(
                "/api/checklists/check",
                new
                {
                    templateId,
                    siteId,
                    itemIndex = 0,
                    done = false,
                }
            )
        ).EnsureSuccessStatusCode();
        today = await owner.GetFromJsonAsync<JsonElement>($"/api/checklists/today?siteId={siteId}");
        Assert.False(
            today.GetProperty("lists")[0].GetProperty("items")[0].GetProperty("done").GetBoolean()
        );

        // another org's member cannot even see the site's checklists (gate 3
        // covers the SITE, and RLS keeps templates apart)
        var outsider = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.GetAsync($"/api/checklists/today?siteId={siteId}")).StatusCode
        );

        // guests hold nothing here
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await fixture.GuestClient().GetAsync($"/api/checklists/today?siteId={siteId}")
            ).StatusCode
        );

        // template deletion is tier 3: config goes
        (
            await owner.DeleteAsync($"/api/checklists/templates/{templateId}")
        ).EnsureSuccessStatusCode();
        today = await owner.GetFromJsonAsync<JsonElement>($"/api/checklists/today?siteId={siteId}");
        Assert.Empty(today.GetProperty("lists").EnumerateArray());
    }
}
