using System.Net;
using System.Text;

namespace LocIntel.IntegrationTests;

/// <summary>
/// ADR 50 §4 for the sites layer: a tile is the scope-filtered answer, private
/// to the principal, empty where nothing is, clustered below the point zoom,
/// and cheap to re-ask for. Tile maths here mirror the client's (Web Mercator).
/// </summary>
public class SiteTilesTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Mvt = "application/vnd.mapbox-vector-tile";

    private static (int x, int y) Tile(double lon, double lat, int z)
    {
        var n = 1 << z;
        var x = (int)Math.Floor((lon + 180) / 360 * n);
        var rad = lat * Math.PI / 180;
        var y = (int)
            Math.Floor((1 - Math.Log(Math.Tan(rad) + 1 / Math.Cos(rad)) / Math.PI) / 2 * n);
        return (x, y);
    }

    private async Task<HttpClient> SeedAsync()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        await ApiFixture.EnsureSiteAsync(
            owner,
            "Hawthorne",
            "America/Los_Angeles",
            45.5122,
            -122.6284
        );
        await ApiFixture.EnsureSiteAsync(
            owner,
            "Pearl District",
            "America/Los_Angeles",
            45.5289,
            -122.6844
        );
        await ApiFixture.EnsureSiteAsync(
            owner,
            "Capitol Hill",
            "America/Los_Angeles",
            47.6191,
            -122.3235
        );
        await ApiFixture.EnsureSiteAsync(owner, "No coordinates");
        return owner;
    }

    [Fact]
    public async Task A_tile_over_a_site_carries_it_and_a_tile_elsewhere_is_empty()
    {
        var owner = await SeedAsync();
        var (x, y) = Tile(-122.6284, 45.5122, 12);

        var tile = await owner.GetAsync($"/api/tiles/sites/12/{x}/{y}");
        Assert.Equal(HttpStatusCode.OK, tile.StatusCode);
        Assert.Equal(Mvt, tile.Content.Headers.ContentType?.MediaType);
        Assert.True(tile.Headers.CacheControl?.Private);
        Assert.Equal(TimeSpan.FromSeconds(60), tile.Headers.CacheControl?.MaxAge);
        Assert.NotNull(tile.Headers.ETag);
        var bytes = await tile.Content.ReadAsByteArrayAsync();
        // MVT keeps string values verbatim in the layer's value table
        Assert.Contains("Hawthorne", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("Capitol Hill", Encoding.UTF8.GetString(bytes));

        var (fx, fy) = Tile(178.4419, -18.1416, 12); // Suva: nothing there
        var empty = await owner.GetAsync($"/api/tiles/sites/12/{fx}/{fy}");
        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);
    }

    [Fact]
    public async Task Below_the_point_zoom_the_tile_carries_clusters_not_sites()
    {
        var owner = await SeedAsync();
        var (x, y) = Tile(-122.6, 45.5, 5); // one tile holds both cities at z5
        var tile = await owner.GetAsync($"/api/tiles/sites/5/{x}/{y}");
        Assert.Equal(HttpStatusCode.OK, tile.StatusCode);
        var text = Encoding.UTF8.GetString(await tile.Content.ReadAsByteArrayAsync());
        Assert.Contains("count", text);
        Assert.DoesNotContain("Hawthorne", text);
    }

    [Fact]
    public async Task A_matching_etag_is_a_304()
    {
        var owner = await SeedAsync();
        var (x, y) = Tile(-122.6284, 45.5122, 12);
        var first = await owner.GetAsync($"/api/tiles/sites/12/{x}/{y}");
        var etag = first.Headers.ETag!.ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/tiles/sites/12/{x}/{y}");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var again = await owner.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
    }

    [Fact]
    public async Task Under_narrows_to_a_node_inside_the_grant_and_bad_tiles_are_400()
    {
        var owner = await SeedAsync();
        var root = await ApiFixture.EnsureRootAsync(owner);
        var (x, y) = Tile(-122.6284, 45.5122, 12);
        var under = await owner.GetAsync($"/api/tiles/sites/12/{x}/{y}?under={root}");
        Assert.Equal(HttpStatusCode.OK, under.StatusCode);
        var unknown = await owner.GetAsync($"/api/tiles/sites/12/{x}/{y}?under={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await owner.GetAsync("/api/tiles/sites/3/99/0")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await owner.GetAsync("/api/tiles/sites/23/0/0")).StatusCode
        );
    }

    [Fact]
    public async Task Another_tenant_sees_nothing_in_the_same_tile()
    {
        await SeedAsync();
        var other = await fixture.LoginAsync(ApiFixture.UserB);
        var (x, y) = Tile(-122.6284, 45.5122, 12);
        var tile = await other.GetAsync($"/api/tiles/sites/12/{x}/{y}");
        Assert.Equal(HttpStatusCode.NoContent, tile.StatusCode);
    }
}
