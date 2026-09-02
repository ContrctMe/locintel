using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>ADR 43: the locator's geo half - ?near sorts by distance, coordless sites sink.</summary>
public class GeoSearchTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Near_sorts_by_distance_and_reports_it()
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
        foreach (
            var (name, lat, lng) in new (string, double?, double?)[]
            {
                ("Cambridge", 42.3736, -71.1097), // ~3 km from downtown Boston
                ("Providence", 41.8240, -71.4128), // ~70 km
                ("No Coords", null, null),
            }
        )
            (
                await owner.PostAsJsonAsync(
                    "/api/sites",
                    new
                    {
                        nodeId = rootId,
                        name,
                        timeZone = "America/New_York",
                        latitude = lat,
                        longitude = lng,
                    }
                )
            ).EnsureSuccessStatusCode();

        // the public host resolves the org (ADR 7): guest + forwarded host
        var guest = fixture.GuestClient();
        guest.DefaultRequestHeaders.Add("X-Forwarded-Host", "org-a.localhost");
        var near = await guest.GetFromJsonAsync<JsonElement>(
            "/public/sites?near=42.3601,-71.0589" // downtown Boston
        );
        var names = near.EnumerateArray().Select(s => s.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(["Cambridge", "Providence", "No Coords"], names);

        var cambridge = near[0];
        var distance = cambridge.GetProperty("distanceKm").GetDouble();
        Assert.InRange(distance, 2, 6);
        Assert.Equal(
            JsonValueKind.Null,
            near[2].GetProperty("distanceKm").ValueKind // no coords: listed, not located
        );

        // garbage near degrades to the plain alphabetical list, never an error
        var garbled = await guest.GetFromJsonAsync<JsonElement>("/public/sites?near=banana");
        Assert.Equal(3, garbled.GetArrayLength());
    }
}
