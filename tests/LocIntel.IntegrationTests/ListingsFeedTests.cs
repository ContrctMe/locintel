using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// ADR 44: the canonical listings export, consumed the way a connector
/// would - with an API key over the integration surface.
/// </summary>
public class ListingsFeedTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Feed_exports_full_listing_records_to_an_api_key()
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
                name = "Feed Site",
                timeZone = "America/New_York",
                addressLine1 = "1 Main St",
                city = "Boston",
                latitude = 42.36,
                longitude = -71.06,
            }
        );
        created.EnsureSuccessStatusCode();
        var siteId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        (
            await owner.PostAsJsonAsync(
                $"/api/sites/{siteId}/schedules",
                new
                {
                    name = "Weekdays",
                    rrule = "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR",
                    anchorDate = "2026-01-05",
                    opens = "09:00",
                    closes = "17:00",
                }
            )
        ).EnsureSuccessStatusCode();

        // a connector consumes with an API key (ADR 40), not a browser session
        var roles = await owner.GetFromJsonAsync<JsonElement>("/api/roles");
        var roleId = roles.EnumerateArray().First().GetProperty("id").GetGuid();
        var key = await owner.PostAsJsonAsync("/api/api-keys", new { name = "listings", roleId });
        key.EnsureSuccessStatusCode();
        var secret = (await key.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("secret")
            .GetString()!;
        var connector = fixture.Factory.CreateDefaultClient();
        connector.DefaultRequestHeaders.Authorization = new("Bearer", secret);

        var feed = await connector.GetFromJsonAsync<JsonElement>("/api/listings/feed");
        Assert.Equal("Org A", feed.GetProperty("organization").GetString());
        var listing = feed.GetProperty("listings")
            .EnumerateArray()
            .First(l => l.GetProperty("name").GetString() == "Feed Site");
        Assert.Equal("1 Main St", listing.GetProperty("addressLine1").GetString());
        Assert.Equal(42.36, listing.GetProperty("latitude").GetDouble());
        Assert.Contains("/sites/", listing.GetProperty("publicUrl").GetString());
        var hours = listing.GetProperty("hours").EnumerateArray().Single();
        Assert.Contains("BYDAY=MO,TU,WE,TH,FR", hours.GetProperty("rRule").GetString());
        Assert.Equal("09:00", hours.GetProperty("opens").GetString());

        // no grant, no feed: a guest is not a connector
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fixture.GuestClient().GetAsync("/api/listings/feed")).StatusCode
        );
    }
}
