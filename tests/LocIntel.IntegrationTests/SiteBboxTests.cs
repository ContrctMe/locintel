using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// ADR 49 against the real PostGIS column: the viewport box is one more
/// predicate on the fleet list - inside the scope, composed with search,
/// split at the antimeridian, naming the sites it can never show, and a 400
/// with a message when malformed.
/// </summary>
public class SiteBboxTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Portland = "-122.8,45.4,-122.5,45.6";

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
            "Capitol Hill",
            "America/Los_Angeles",
            47.6191,
            -122.3235
        );
        await ApiFixture.EnsureSiteAsync(owner, "No coordinates");
        await ApiFixture.EnsureSiteAsync(owner, "Suva", "Pacific/Fiji", -18.1416, 178.4419);
        await ApiFixture.EnsureSiteAsync(owner, "Apia", "Pacific/Apia", -13.8333, -171.7667);
        return owner;
    }

    private static string[] Names(JsonElement list) =>
        list.GetProperty("items")
            .EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()!)
            .Order()
            .ToArray();

    [Fact]
    public async Task Box_filters_to_sites_inside_it_and_names_the_uncoordinated()
    {
        var owner = await SeedAsync();

        var inBox = await owner.GetFromJsonAsync<JsonElement>($"/api/sites?bbox={Portland}");
        Assert.Equal(["Hawthorne"], Names(inBox));
        Assert.Equal(1, inBox.GetProperty("total").GetInt32());
        Assert.Equal(1, inBox.GetProperty("withoutCoordinates").GetInt32());

        // no box: the whole scope, and the count is absent rather than zero
        var all = await owner.GetFromJsonAsync<JsonElement>("/api/sites");
        Assert.Equal(5, all.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, all.GetProperty("withoutCoordinates").ValueKind);
    }

    [Fact]
    public async Task Box_composes_with_search()
    {
        var owner = await SeedAsync();
        var none = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/sites?bbox={Portland}&q=Capitol"
        );
        Assert.Empty(Names(none));
        Assert.Equal(0, none.GetProperty("total").GetInt32());
        var one = await owner.GetFromJsonAsync<JsonElement>($"/api/sites?bbox={Portland}&q=hawth");
        Assert.Equal(["Hawthorne"], Names(one));
    }

    [Fact]
    public async Task A_box_across_the_antimeridian_finds_both_sides()
    {
        var owner = await SeedAsync();
        var pacific = await owner.GetFromJsonAsync<JsonElement>("/api/sites?bbox=175,-20,-170,-10");
        Assert.Equal(["Apia", "Suva"], Names(pacific));
    }

    [Fact]
    public async Task A_planet_sized_box_returns_exactly_the_coordinated_scope()
    {
        var owner = await SeedAsync();
        var planet = await owner.GetFromJsonAsync<JsonElement>("/api/sites?bbox=-180,-90,180,90");
        Assert.Equal(["Apia", "Capitol Hill", "Hawthorne", "Suva"], Names(planet));
        Assert.Equal(1, planet.GetProperty("withoutCoordinates").GetInt32());
    }

    [Theory]
    [InlineData("bbox=1,2,3", "bbox must be west,south,east,north")]
    [InlineData("bbox=-200,0,10,10", "bbox longitude out of range")]
    [InlineData("bbox=0,50,10,10", "bbox south must not exceed north")]
    [InlineData("bbox=" + Portland + "&zoom=30", "zoom must be between 0 and 22")]
    public async Task Malformed_input_is_a_400_with_a_message(string query, string message)
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var response = await owner.GetAsync($"/api/sites?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(message, body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Zoom_is_accepted_and_changes_nothing_yet()
    {
        var owner = await SeedAsync();
        var withZoom = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/sites?bbox={Portland}&zoom=12"
        );
        Assert.Equal(["Hawthorne"], Names(withZoom));
    }
}
