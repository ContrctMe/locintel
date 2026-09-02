using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Routing reaches only vendors that serve the site (area and radius), and
/// the SLA sweep widens an unanswered broadcast to the next vendors and
/// nudges the buyer on an unanswered direct request.
/// </summary>
public class MarketplaceRoutingTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Vendors_are_matched_by_area_and_radius_and_escalation_widens_a_broadcast()
    {
        var buyer = await fixture.LoginAsync(ApiFixture.UserA);
        var la = await fixture.LoginAsync(ApiFixture.UserB); // org B: an LA vendor, 50 km radius, US only
        var ops = await fixture.OperatorClient(); // the platform org doubles as a nationwide vendor here
        (
            await la.PutAsJsonAsync(
                "/api/vendor/profile",
                new
                {
                    name = "LA Rapid Guard",
                    categories = new[] { "GuardService" },
                    serviceAreas = new[] { "US" },
                    latitude = 34.05,
                    longitude = -118.25,
                    serviceRadiusKm = 50,
                }
            )
        ).EnsureSuccessStatusCode();
        (
            await la.PostAsJsonAsync("/api/vendor/profile/publish", new { published = true })
        ).EnsureSuccessStatusCode();
        (
            await ops.PutAsJsonAsync(
                "/api/vendor/profile",
                new { name = "Nationwide Guard", categories = new[] { "GuardService" } }
            )
        ).EnsureSuccessStatusCode();
        (
            await ops.PostAsJsonAsync("/api/vendor/profile/publish", new { published = true })
        ).EnsureSuccessStatusCode();

        var hierarchy = await buyer.GetAsync("/api/hierarchy");
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
            var created = await buyer.PostAsJsonAsync(
                "/api/hierarchy",
                new { name = "Org A", levels = new[] { "Region" } }
            );
            created.EnsureSuccessStatusCode();
            rootId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("rootNodeId")
                .GetGuid();
        }
        async Task<Guid> Site(string name, double lat, double lng, string country)
        {
            var r = await buyer.PostAsJsonAsync(
                "/api/sites",
                new
                {
                    nodeId = rootId,
                    name,
                    timeZone = "America/Los_Angeles",
                    latitude = lat,
                    longitude = lng,
                    countryCode = country,
                }
            );
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
        var downtown = await Site("Downtown LA", 34.0522, -118.2437, "US");
        var nyc = await Site("Midtown NYC", 40.7549, -73.984, "US");
        var toronto = await Site("Toronto", 43.6532, -79.3832, "CA");

        // NYC is outside the LA vendor's radius: only the nationwide vendor is reached
        var far = await buyer.PostAsJsonAsync(
            "/api/marketplace/requests",
            new
            {
                category = "GuardService",
                urgency = "Scheduled",
                siteId = nyc,
                title = "NYC guard",
                startsAt = DateTimeOffset.UtcNow.AddDays(1),
            }
        );
        far.EnsureSuccessStatusCode();
        var farId = (await far.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        (
            await buyer.PostAsync($"/api/marketplace/requests/{farId}/submit", null)
        ).EnsureSuccessStatusCode();
        var farDetail = await buyer.GetFromJsonAsync<JsonElement>(
            $"/api/marketplace/requests/{farId}"
        );
        var farRecipients = farDetail
            .GetProperty("recipients")
            .EnumerateArray()
            .Select(r => r.GetProperty("vendorName").GetString())
            .ToList();
        Assert.Equal(new[] { "Nationwide Guard" }, farRecipients);
        Assert.NotEqual(JsonValueKind.Null, farDetail.GetProperty("responseDueAt").ValueKind);
        // Toronto is outside the LA vendor's areas (US only); the nationwide one still serves it
        var ca = await buyer.PostAsJsonAsync(
            "/api/marketplace/requests",
            new
            {
                category = "GuardService",
                urgency = "Scheduled",
                siteId = toronto,
                title = "Toronto guard",
                startsAt = DateTimeOffset.UtcNow.AddDays(1),
            }
        );
        ca.EnsureSuccessStatusCode();
        // a direct request to a vendor that does not reach the site still works (the buyer chose them)
        var direct = await buyer.PostAsJsonAsync(
            "/api/marketplace/requests",
            new
            {
                vendorOrgId = fixture.OrgB.Value,
                category = "GuardService",
                urgency = "Emergency",
                siteId = nyc,
                title = "Chosen anyway",
                startsAt = DateTimeOffset.UtcNow.AddHours(1),
            }
        );
        Assert.True(direct.IsSuccessStatusCode, await direct.Content.ReadAsStringAsync());
        var directId = (await direct.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        (
            await buyer.PostAsync($"/api/marketplace/requests/{directId}/submit", null)
        ).EnsureSuccessStatusCode();

        // downtown LA: both match, but the fixture starts broadcasts at ONE recipient (nearest first)
        var near = await buyer.PostAsJsonAsync(
            "/api/marketplace/requests",
            new
            {
                category = "GuardService",
                urgency = "Emergency",
                siteId = downtown,
                title = "Downtown guard now",
                startsAt = DateTimeOffset.UtcNow.AddHours(1),
            }
        );
        near.EnsureSuccessStatusCode();
        var nearId = (await near.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        (
            await buyer.PostAsync($"/api/marketplace/requests/{nearId}/submit", null)
        ).EnsureSuccessStatusCode();
        var nearDetail = await buyer.GetFromJsonAsync<JsonElement>(
            $"/api/marketplace/requests/{nearId}"
        );
        Assert.Equal(
            new[] { "LA Rapid Guard" },
            nearDetail
                .GetProperty("recipients")
                .EnumerateArray()
                .Select(r => r.GetProperty("vendorName").GetString())
                .ToArray()
        );

        // the SLA sweep (windows are zero in the fixture) widens the broadcast and reminds the direct vendor
        var sweep = await buyer.PostAsync("/api/marketplace/requests/sla/sweep", null);
        sweep.EnsureSuccessStatusCode();
        Assert.True(
            (await sweep.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("overdue").GetInt32()
                >= 2
        );
        var widened = false;
        for (var i = 0; i < 50 && !widened; i++)
        {
            await Task.Delay(100);
            nearDetail = await buyer.GetFromJsonAsync<JsonElement>(
                $"/api/marketplace/requests/{nearId}"
            );
            widened = nearDetail.GetProperty("recipients").GetArrayLength() == 2;
        }
        Assert.True(widened, "the broadcast should have been widened to the next vendor");
        Assert.Equal(1, nearDetail.GetProperty("escalationCount").GetInt32());
        Assert.Contains(
            nearDetail.GetProperty("events").EnumerateArray(),
            e => (e.GetProperty("body").GetString() ?? "").Contains("widened")
        );
        // the nationwide vendor now sees it in the queue and can quote; the LA vendor still can too
        var opsQueue = await ops.GetFromJsonAsync<JsonElement>("/api/vendor/requests");
        Assert.Contains(
            opsQueue.GetProperty("items").EnumerateArray(),
            r => r.GetProperty("id").GetGuid() == nearId
        );
        var directDetail = await buyer.GetFromJsonAsync<JsonElement>(
            $"/api/marketplace/requests/{directId}"
        );
        Assert.Equal(1, directDetail.GetProperty("escalationCount").GetInt32());
        Assert.Contains(
            directDetail.GetProperty("events").EnumerateArray(),
            e => (e.GetProperty("body").GetString() ?? "").Contains("reminded")
        );
    }
}
