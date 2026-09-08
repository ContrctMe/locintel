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
        var rootId = await ApiFixture.EnsureRootAsync(owner, "Org A");
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

    [Fact]
    public async Task Bulk_status_changes_the_chosen_sites_and_counts_what_it_skipped()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        await ApiFixture.EnsureRootAsync(owner, "Org A");
        var ids = new List<Guid>();
        foreach (var name in new[] { "Bulk One", "Bulk Two" })
            ids.Add(await ApiFixture.EnsureSiteAsync(owner, name));

        var response = await owner.PostAsJsonAsync(
            "/api/sites/bulk-status",
            new { ids = new[] { ids[0], ids[1], Guid.NewGuid() }, status = "TemporarilyClosed" }
        );
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, result.GetProperty("updated").GetInt32());
        Assert.Equal(1, result.GetProperty("skipped").GetInt32());
        foreach (var id in ids)
            Assert.Equal(
                "TemporarilyClosed",
                (await owner.GetFromJsonAsync<JsonElement>($"/api/sites/{id}"))
                    .GetProperty("status")
                    .GetString()
            );

        // back to open, and an empty choice is a request error, not a no-op
        (
            await owner.PostAsJsonAsync("/api/sites/bulk-status", new { ids, status = "Open" })
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            System.Net.HttpStatusCode.BadRequest,
            (
                await owner.PostAsJsonAsync(
                    "/api/sites/bulk-status",
                    new { ids = Array.Empty<Guid>(), status = "Open" }
                )
            ).StatusCode
        );
    }
}
