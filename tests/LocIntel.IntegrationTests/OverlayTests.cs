using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// ADR 50 §3-4 for the Spatial module: overlays are the org's own rows,
/// anchored to a hierarchy node whose path is their scope; uploads are
/// polygons only; the layer serves as vector tiles behind the same gates as
/// sites; the trash round-trips; another tenant sees nothing.
/// </summary>
public class OverlayTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Mvt = "application/vnd.mapbox-vector-tile";

    // a rough square around central Portland
    private static readonly string Portland = """
        {"type":"FeatureCollection","features":[
          {"type":"Feature","properties":{"name":"Downtown","priority":1},
           "geometry":{"type":"Polygon","coordinates":[[[-122.70,45.50],[-122.65,45.50],[-122.65,45.54],[-122.70,45.54],[-122.70,45.50]]]}}
        ]}
        """;

    private static (int x, int y) Tile(double lon, double lat, int z)
    {
        var n = 1 << z;
        var x = (int)Math.Floor((lon + 180) / 360 * n);
        var rad = lat * Math.PI / 180;
        var y = (int)
            Math.Floor((1 - Math.Log(Math.Tan(rad) + 1 / Math.Cos(rad)) / Math.PI) / 2 * n);
        return (x, y);
    }

    private static StringContent Upload(string geoJson) =>
        new(
            JsonSerializer.Serialize(new { geoJson = JsonDocument.Parse(geoJson).RootElement }),
            Encoding.UTF8,
            "application/json"
        );

    private static async Task<Guid> CreateLayerAsync(
        HttpClient client,
        string name,
        Guid? nodeId = null
    )
    {
        var created = await client.PostAsJsonAsync(
            "/api/overlays",
            new
            {
                name,
                kind = "Territory",
                style = new { fill = "#7c6cf0", opacity = 0.2 },
                nodeId,
            }
        );
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task A_layer_takes_polygons_and_serves_them_as_a_tile()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        await ApiFixture.EnsureRootAsync(owner);
        var id = await CreateLayerAsync(
            owner,
            "Delivery zones " + Guid.NewGuid().ToString("N")[..6]
        );

        var replaced = await owner.PutAsync($"/api/overlays/{id}/features", Upload(Portland));
        Assert.True(replaced.IsSuccessStatusCode, await replaced.Content.ReadAsStringAsync());
        var state = await replaced.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, state.GetProperty("count").GetInt32());
        Assert.Equal(1, state.GetProperty("version").GetInt32());

        var list = await owner.GetFromJsonAsync<JsonElement>("/api/overlays");
        var layer = list.GetProperty("layers")
            .EnumerateArray()
            .Single(l => l.GetProperty("id").GetGuid() == id);
        Assert.Equal("territory", layer.GetProperty("kind").GetString());
        Assert.Equal(1, layer.GetProperty("featureCount").GetInt32());
        Assert.Equal("#7c6cf0", layer.GetProperty("style").GetProperty("fill").GetString());

        var (x, y) = Tile(-122.675, 45.52, 12);
        var tile = await owner.GetAsync($"/api/tiles/overlays/{id}/12/{x}/{y}");
        Assert.Equal(HttpStatusCode.OK, tile.StatusCode);
        Assert.Equal(Mvt, tile.Content.Headers.ContentType?.MediaType);
        Assert.True(tile.Headers.CacheControl?.Private);
        Assert.NotNull(tile.Headers.ETag);
        var text = Encoding.UTF8.GetString(await tile.Content.ReadAsByteArrayAsync());
        // properties ride along, expanded from jsonb
        Assert.Contains("Downtown", text);
        Assert.Contains("priority", text);

        var (fx, fy) = Tile(178.4419, -18.1416, 12); // Suva: nothing there
        var empty = await owner.GetAsync($"/api/tiles/overlays/{id}/12/{fx}/{fy}");
        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);
    }

    [Fact]
    public async Task Bad_uploads_are_400_with_a_reason()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        await ApiFixture.EnsureRootAsync(owner);
        var id = await CreateLayerAsync(owner, "Rejects " + Guid.NewGuid().ToString("N")[..6]);

        async Task<string> Reject(string geoJson)
        {
            var response = await owner.PutAsync($"/api/overlays/{id}/features", Upload(geoJson));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }

        Assert.Contains(
            "polygons",
            await Reject("""{"type":"Point","coordinates":[-122.6,45.5]}""")
        );
        Assert.Contains(
            "WGS84",
            await Reject(
                """{"type":"Polygon","coordinates":[[[500,0],[501,0],[501,1],[500,1],[500,0]]]}"""
            )
        );
        Assert.Contains(
            "not valid",
            await Reject("""{"type":"Polygon","coordinates":[[[0,0],[1,1],[1,0],[0,1],[0,0]]]}""")
        );
        Assert.Contains(
            "no features",
            await Reject("""{"type":"FeatureCollection","features":[]}""")
        );
    }

    [Fact]
    public async Task The_trash_round_trips_and_hides_the_tile_meanwhile()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        await ApiFixture.EnsureRootAsync(owner);
        var id = await CreateLayerAsync(owner, "Trashed " + Guid.NewGuid().ToString("N")[..6]);
        (
            await owner.PutAsync($"/api/overlays/{id}/features", Upload(Portland))
        ).EnsureSuccessStatusCode();
        var (x, y) = Tile(-122.675, 45.52, 12);

        var deleted = await owner.DeleteAsync($"/api/overlays/{id}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.NotEqual(
            JsonValueKind.Null,
            (await deleted.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("deletedAt")
                .ValueKind
        );
        var live = await owner.GetFromJsonAsync<JsonElement>("/api/overlays");
        Assert.DoesNotContain(
            live.GetProperty("layers").EnumerateArray(),
            l => l.GetProperty("id").GetGuid() == id
        );
        var trash = await owner.GetFromJsonAsync<JsonElement>("/api/overlays?deleted=true");
        Assert.Contains(
            trash.GetProperty("layers").EnumerateArray(),
            l => l.GetProperty("id").GetGuid() == id
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await owner.GetAsync($"/api/tiles/overlays/{id}/12/{x}/{y}")).StatusCode
        );

        var restored = await owner.PostAsync($"/api/overlays/{id}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await owner.GetAsync($"/api/tiles/overlays/{id}/12/{x}/{y}")).StatusCode
        );
        // the features came back with the layer
        var list = await owner.GetFromJsonAsync<JsonElement>("/api/overlays");
        Assert.Equal(
            1,
            list.GetProperty("layers")
                .EnumerateArray()
                .Single(l => l.GetProperty("id").GetGuid() == id)
                .GetProperty("featureCount")
                .GetInt32()
        );
    }

    [Fact]
    public async Task Another_tenant_sees_neither_the_layer_nor_its_tile()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        await ApiFixture.EnsureRootAsync(owner);
        var id = await CreateLayerAsync(owner, "Private " + Guid.NewGuid().ToString("N")[..6]);
        (
            await owner.PutAsync($"/api/overlays/{id}/features", Upload(Portland))
        ).EnsureSuccessStatusCode();

        var other = await fixture.LoginAsync(ApiFixture.UserB);
        var (x, y) = Tile(-122.675, 45.52, 12);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await other.GetAsync($"/api/tiles/overlays/{id}/12/{x}/{y}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await other.PutAsync($"/api/overlays/{id}/features", Upload(Portland))).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await other.DeleteAsync($"/api/overlays/{id}")).StatusCode
        );
        var list = await other.GetFromJsonAsync<JsonElement>("/api/overlays");
        Assert.DoesNotContain(
            list.GetProperty("layers").EnumerateArray(),
            l => l.GetProperty("id").GetGuid() == id
        );
    }

    [Fact]
    public async Task An_unknown_anchor_is_404_and_an_out_of_range_tile_is_400()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        await ApiFixture.EnsureRootAsync(owner);
        var unknown = await owner.PostAsJsonAsync(
            "/api/overlays",
            new
            {
                name = "Nowhere",
                kind = "zone",
                nodeId = Guid.NewGuid(),
            }
        );
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var id = await CreateLayerAsync(owner, "Range " + Guid.NewGuid().ToString("N")[..6]);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await owner.GetAsync($"/api/tiles/overlays/{id}/3/99/0")).StatusCode
        );
    }
}
