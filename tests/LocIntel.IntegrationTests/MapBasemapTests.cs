using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// ADR 50 §3: the org setting <c>map.basemaps</c>. The key goes in once,
/// lives encrypted, never comes back to the settings page, and is in the
/// URL the map is handed - which is what a tile provider wants.
/// </summary>
public class MapBasemapTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task The_key_is_stored_encrypted_hidden_from_settings_and_substituted_for_the_map()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var put = await owner.PutAsJsonAsync(
            "/api/map/basemaps",
            new
            {
                basemaps = new object[]
                {
                    new
                    {
                        id = "aerial",
                        name = "Aerial",
                        urlTemplate = "https://tiles.example.com/aerial/{z}/{x}/{y}.jpg?key={key}",
                        attribution = "© Example",
                        maxZoom = 20,
                        key = "sk_live_1234",
                    },
                    new
                    {
                        id = "terrain",
                        name = "Terrain",
                        urlTemplate = "https://tiles.example.com/terrain/{z}/{x}/{y}.png",
                        attribution = "© Example",
                    },
                },
            }
        );
        Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());
        var settings = await put.Content.ReadFromJsonAsync<JsonElement>();
        var aerial = settings.GetProperty("basemaps")[0];
        Assert.True(aerial.GetProperty("hasKey").GetBoolean());
        Assert.False(settings.GetProperty("basemaps")[1].GetProperty("hasKey").GetBoolean());
        Assert.Equal(19, settings.GetProperty("basemaps")[1].GetProperty("maxZoom").GetInt32());
        Assert.DoesNotContain("sk_live_1234", await put.Content.ReadAsStringAsync());

        // the generic settings store holds a cipher, not the key
        var raw = await owner.GetFromJsonAsync<JsonElement>("/api/settings");
        var stored = raw.EnumerateArray()
            .Single(s => s.GetProperty("key").GetString() == "map.basemaps")
            .GetProperty("value")
            .GetString()!;
        Assert.DoesNotContain("sk_live_1234", stored);
        Assert.Contains("keyCipher", stored);

        // the map gets the key in the URL
        var forMap = await owner.GetFromJsonAsync<JsonElement>("/api/map/basemaps");
        var url = forMap.GetProperty("basemaps")[0].GetProperty("urlTemplate").GetString();
        Assert.Equal("https://tiles.example.com/aerial/{z}/{x}/{y}.jpg?key=sk_live_1234", url);

        // a second save without the key keeps the stored one
        var again = await owner.PutAsJsonAsync(
            "/api/map/basemaps",
            new
            {
                basemaps = new[]
                {
                    new
                    {
                        id = "aerial",
                        name = "Aerial imagery",
                        urlTemplate = "https://tiles.example.com/aerial/{z}/{x}/{y}.jpg?key={key}",
                        attribution = "© Example",
                    },
                },
            }
        );
        Assert.True(again.IsSuccessStatusCode, await again.Content.ReadAsStringAsync());
        forMap = await owner.GetFromJsonAsync<JsonElement>("/api/map/basemaps");
        Assert.Single(forMap.GetProperty("basemaps").EnumerateArray());
        Assert.Contains(
            "key=sk_live_1234",
            forMap.GetProperty("basemaps")[0].GetProperty("urlTemplate").GetString()
        );

        // another tenant has its own (empty) list
        var other = await fixture.LoginAsync(ApiFixture.UserB);
        var theirs = await other.GetFromJsonAsync<JsonElement>("/api/map/basemaps");
        Assert.Empty(theirs.GetProperty("basemaps").EnumerateArray());
    }

    [Fact]
    public async Task Bad_entries_are_400_with_the_reason()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);

        async Task<string> Reject(object entry)
        {
            var response = await owner.PutAsJsonAsync(
                "/api/map/basemaps",
                new { basemaps = new[] { entry } }
            );
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }

        Assert.Contains(
            "https",
            await Reject(
                new
                {
                    id = "plain",
                    name = "Plain",
                    urlTemplate = "http://tiles.example.com/{z}/{x}/{y}.png",
                    attribution = "",
                }
            )
        );
        Assert.Contains(
            "{z}",
            await Reject(
                new
                {
                    id = "nozxy",
                    name = "No tiles",
                    urlTemplate = "https://tiles.example.com/static.png",
                    attribution = "",
                }
            )
        );
        Assert.Contains(
            "no key",
            await Reject(
                new
                {
                    id = "keyed",
                    name = "Keyed",
                    urlTemplate = "https://tiles.example.com/{z}/{x}/{y}.png?k={key}",
                    attribution = "",
                }
            )
        );
        Assert.Contains(
            "lowercase",
            await Reject(
                new
                {
                    id = "Not Valid",
                    name = "Bad id",
                    urlTemplate = "https://tiles.example.com/{z}/{x}/{y}.png",
                    attribution = "",
                }
            )
        );
    }
}
