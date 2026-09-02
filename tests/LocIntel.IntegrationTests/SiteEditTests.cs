using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Competitive-review finding 2: an address typo must be fixable. Patch
/// semantics - null leaves a field alone, empty string clears it.
/// </summary>
public class SiteEditTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Address_is_editable_and_clearable_after_create()
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
                name = "Typo Depot",
                timeZone = "Etc/UTC",
                addressLine1 = "123 Wrnog St",
                city = "Boston",
            }
        );
        created.EnsureSuccessStatusCode();
        var siteId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        // fix the typo; untouched fields (city) survive the patch
        (
            await owner.PostAsJsonAsync(
                $"/api/sites/{siteId}",
                new
                {
                    addressLine1 = "123 Wrong St",
                    postalCode = "02101",
                    countryCode = "us",
                }
            )
        ).EnsureSuccessStatusCode();
        var fixedUp = await owner.GetFromJsonAsync<JsonElement>($"/api/sites/{siteId}");
        Assert.Equal("123 Wrong St", fixedUp.GetProperty("addressLine1").GetString());
        Assert.Equal("Boston", fixedUp.GetProperty("city").GetString());
        Assert.Equal("US", fixedUp.GetProperty("countryCode").GetString()); // normalized

        // empty string CLEARS; omitted fields still survive
        (
            await owner.PostAsJsonAsync($"/api/sites/{siteId}", new { postalCode = "" })
        ).EnsureSuccessStatusCode();
        var cleared = await owner.GetFromJsonAsync<JsonElement>($"/api/sites/{siteId}");
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("postalCode").ValueKind);
        Assert.Equal("123 Wrong St", cleared.GetProperty("addressLine1").GetString());
    }
}
