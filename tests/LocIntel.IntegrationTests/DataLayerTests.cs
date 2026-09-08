using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// ADR 50 §3-4: data layers are registered queries, listed for the principal
/// that may read them and served as tiles at one route. Each site here sits
/// alone in its tile (a far-off place per test) so the status the tile
/// carries is this test's, not a neighbour's.
/// </summary>
public class DataLayerTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static (int x, int y) Tile(double lon, double lat, int z)
    {
        var n = 1 << z;
        var x = (int)Math.Floor((lon + 180) / 360 * n);
        var rad = lat * Math.PI / 180;
        var y = (int)
            Math.Floor((1 - Math.Log(Math.Tan(rad) + 1 / Math.Cos(rad)) / Math.PI) / 2 * n);
        return (x, y);
    }

    private static async Task<string?> TileTextAsync(
        HttpClient client,
        string layer,
        double lon,
        double lat
    )
    {
        var (x, y) = Tile(lon, lat, 14);
        var tile = await client.GetAsync($"/api/tiles/{layer}/14/{x}/{y}");
        if (tile.StatusCode == HttpStatusCode.NoContent)
            return null;
        Assert.Equal(HttpStatusCode.OK, tile.StatusCode);
        return Encoding.UTF8.GetString(await tile.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task The_registry_lists_the_layers_the_principal_may_read()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var list = await owner.GetFromJsonAsync<JsonElement>("/api/map/layers");
        var names = list.GetProperty("layers")
            .EnumerateArray()
            .Select(l => l.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("sites", names);
        Assert.Contains("open-now", names);
        Assert.Contains("checklists-today", names);
        var sites = list.GetProperty("layers")
            .EnumerateArray()
            .Single(l => l.GetProperty("name").GetString() == "sites");
        Assert.Equal("sites:read", sites.GetProperty("capability").GetString());
        Assert.Contains(
            sites.GetProperty("statuses").EnumerateArray(),
            s => s.GetProperty("key").GetString() == "Open"
        );

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.GetAsync("/api/tiles/no-such-layer/3/1/1")).StatusCode
        );
    }

    [Fact]
    public async Task World_and_continent_tiles_carry_the_sites_as_clusters()
    {
        // ADR 51: a zoom-0 or zoom-1 envelope is 360 or 180 degrees wide, which
        // as a geography polygon is empty or an antipodal error; as a cell
        // range it is just the widest range there is
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        await ApiFixture.EnsureSiteAsync(
            owner,
            "Ushuaia",
            "America/Argentina/Ushuaia",
            -54.8,
            -68.3
        );

        var world = await owner.GetAsync("/api/tiles/sites/0/0/0");
        Assert.Equal(HttpStatusCode.OK, world.StatusCode);
        Assert.Contains(
            "count",
            Encoding.UTF8.GetString(await world.Content.ReadAsByteArrayAsync())
        );

        // the south-west quarter of the planet at zoom 1 holds Ushuaia
        var (x, y) = Tile(-68.3, -54.8, 1);
        var quarter = await owner.GetAsync($"/api/tiles/sites/1/{x}/{y}");
        Assert.Equal(HttpStatusCode.OK, quarter.StatusCode);
        // and the opposite quarter answers, empty or not, never an error
        var elsewhere = await owner.GetAsync($"/api/tiles/sites/1/{1 - x}/{1 - y}");
        Assert.True(
            elsewhere.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK,
            elsewhere.StatusCode.ToString()
        );
    }

    [Fact]
    public async Task Open_now_reads_the_occurrence_projection()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        // Nuuk: nothing else lives in this tile
        const double lon = -51.72;
        const double lat = 64.18;
        var siteId = await ApiFixture.EnsureSiteAsync(owner, "Nuuk depot", "Etc/UTC", lat, lon);

        var before = await TileTextAsync(owner, "open-now", lon, lat);
        Assert.NotNull(before);
        Assert.Contains("unscheduled", before);

        var schedule = await owner.PostAsJsonAsync(
            $"/api/sites/{siteId}/schedules",
            new
            {
                name = "All day",
                rrule = "FREQ=DAILY",
                anchorDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                opens = "00:00",
                closes = "23:59",
            }
        );
        Assert.True(schedule.IsSuccessStatusCode, await schedule.Content.ReadAsStringAsync());

        // the projection is rebuilt by a message; the tile follows it
        string? after = null;
        await ApiFixture.WaitUntilAsync(
            async () =>
                (after = await TileTextAsync(owner, "open-now", lon, lat))?.Contains("open") == true
                && !after.Contains("unscheduled"),
            "the open-now tile to show the site open",
            diagnostics: fixture.DeadLetterSummary
        );
    }

    [Fact]
    public async Task Checklists_today_follows_the_sites_own_business_date()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        // Kiritimati: alone in its tile, and the furthest-ahead clock there is
        const double lon = -157.36;
        const double lat = 1.87;
        var siteId = await ApiFixture.EnsureSiteAsync(
            owner,
            "Kiritimati",
            "Pacific/Kiritimati",
            lat,
            lon
        );

        Assert.Contains("none", (await TileTextAsync(owner, "checklists-today", lon, lat))!);

        var template = await owner.PostAsJsonAsync(
            "/api/checklists/templates",
            new { name = "Kiritimati opening", items = new[] { "Unlock", "Lights" } }
        );
        Assert.True(template.IsSuccessStatusCode, await template.Content.ReadAsStringAsync());
        var templateId = (await template.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        Assert.Contains("pending", (await TileTextAsync(owner, "checklists-today", lon, lat))!);

        async Task Check(int index)
        {
            var response = await owner.PostAsJsonAsync(
                "/api/checklists/check",
                new
                {
                    templateId,
                    siteId,
                    itemIndex = index,
                    done = true,
                }
            );
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }

        await Check(0);
        Assert.Contains("partial", (await TileTextAsync(owner, "checklists-today", lon, lat))!);
        await Check(1);
        var text = (await TileTextAsync(owner, "checklists-today", lon, lat))!;
        Assert.Contains("complete", text);
        Assert.DoesNotContain("partial", text);

        // another tenant's tile over the same place is empty
        var other = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Null(await TileTextAsync(other, "checklists-today", lon, lat));
    }
}
