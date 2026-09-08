using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The console's filter bar narrows the site list by lifecycle status on the
/// server, so the table and the map read the same rows.
/// </summary>
public class SiteStatusFilterTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Status_narrows_the_list_and_an_unknown_status_is_400()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        await ApiFixture.EnsureSiteAsync(owner, "Status filter probe");

        var open = await owner.GetFromJsonAsync<JsonElement>("/api/sites?status=Open&limit=200");
        Assert.All(
            open.GetProperty("items").EnumerateArray(),
            s => Assert.Equal("Open", s.GetProperty("status").GetString())
        );
        Assert.Contains(
            open.GetProperty("items").EnumerateArray(),
            s => s.GetProperty("name").GetString() == "Status filter probe"
        );

        var closed = await owner.GetFromJsonAsync<JsonElement>(
            "/api/sites?status=Closed,TemporarilyClosed&limit=200"
        );
        Assert.DoesNotContain(
            closed.GetProperty("items").EnumerateArray(),
            s => s.GetProperty("status").GetString() == "Open"
        );

        var bogus = await owner.GetAsync("/api/sites?status=Bogus");
        Assert.Equal(HttpStatusCode.BadRequest, bogus.StatusCode);
    }
}
