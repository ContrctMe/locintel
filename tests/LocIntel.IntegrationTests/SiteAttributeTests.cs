using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// ADR 46: the tenant's own site schema. Definitions gate what values are
/// accepted (typed), the Public flag gates what the public page shows, and
/// deleting a definition takes its values with it.
/// </summary>
public class SiteAttributeTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Org_defined_attributes_validate_store_and_expose_by_visibility()
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
                name = "Attributed",
                timeZone = "Etc/UTC",
            }
        );
        created.EnsureSuccessStatusCode();
        var siteId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        // define the org's schema: one public, one internal
        var driveThru = await owner.PostAsJsonAsync(
            "/api/sites/attributes",
            new
            {
                key = "drive_thru",
                label = "Drive-thru",
                type = "Boolean",
                @public = true,
            }
        );
        Assert.True(driveThru.IsSuccessStatusCode, await driveThru.Content.ReadAsStringAsync());
        (
            await owner.PostAsJsonAsync(
                "/api/sites/attributes",
                new
                {
                    key = "cost_center",
                    label = "Cost center",
                    type = "Text",
                }
            )
        ).EnsureSuccessStatusCode();
        // duplicate key: conflict
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await owner.PostAsJsonAsync(
                    "/api/sites/attributes",
                    new
                    {
                        key = "drive_thru",
                        label = "Again",
                        type = "Text",
                    }
                )
            ).StatusCode
        );

        // values validate against the definitions
        var wrongType = await owner.PostAsJsonAsync(
            $"/api/sites/{siteId}",
            new { attributes = new { drive_thru = "yes" } }
        );
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
        var unknown = await owner.PostAsJsonAsync(
            $"/api/sites/{siteId}",
            new { attributes = new { parking = 40 } }
        );
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        (
            await owner.PostAsJsonAsync(
                $"/api/sites/{siteId}",
                new { attributes = new { drive_thru = true, cost_center = "CC-104" } }
            )
        ).EnsureSuccessStatusCode();
        var site = await owner.GetFromJsonAsync<JsonElement>($"/api/sites/{siteId}");
        Assert.True(site.GetProperty("attributes").GetProperty("drive_thru").GetBoolean());
        Assert.Equal(
            "CC-104",
            site.GetProperty("attributes").GetProperty("cost_center").GetString()
        );

        // patch-merge: touching one key leaves the other alone; null removes
        (
            await owner.PostAsJsonAsync(
                $"/api/sites/{siteId}",
                new { attributes = new { cost_center = (string?)null } }
            )
        ).EnsureSuccessStatusCode();
        site = await owner.GetFromJsonAsync<JsonElement>($"/api/sites/{siteId}");
        Assert.True(site.GetProperty("attributes").GetProperty("drive_thru").GetBoolean());
        Assert.False(site.GetProperty("attributes").TryGetProperty("cost_center", out _));

        // restore for the visibility check
        (
            await owner.PostAsJsonAsync(
                $"/api/sites/{siteId}",
                new { attributes = new { cost_center = "CC-104" } }
            )
        ).EnsureSuccessStatusCode();

        // the public page sees ONLY the public attribute, with its label
        var guest = fixture.GuestClient();
        guest.DefaultRequestHeaders.Add("X-Forwarded-Host", "org-a.localhost");
        var publicSite = await guest.GetFromJsonAsync<JsonElement>($"/public/sites/{siteId}");
        var publicAttribute = publicSite.GetProperty("attributes").EnumerateArray().Single();
        Assert.Equal("drive_thru", publicAttribute.GetProperty("key").GetString());
        Assert.Equal("Drive-thru", publicAttribute.GetProperty("label").GetString());

        // the listings feed carries everything (connectors are org-side)
        var feed = await owner.GetFromJsonAsync<JsonElement>("/api/listings/feed");
        var listing = feed.GetProperty("listings")
            .EnumerateArray()
            .First(l => l.GetProperty("name").GetString() == "Attributed");
        Assert.Equal(
            "CC-104",
            listing.GetProperty("attributes").GetProperty("cost_center").GetString()
        );

        // deleting a definition strips its values everywhere
        var definitions = await owner.GetFromJsonAsync<JsonElement>("/api/sites/attributes");
        var costCenterId = definitions
            .EnumerateArray()
            .First(d => d.GetProperty("key").GetString() == "cost_center")
            .GetProperty("id")
            .GetGuid();
        (
            await owner.DeleteAsync($"/api/sites/attributes/{costCenterId}")
        ).EnsureSuccessStatusCode();
        site = await owner.GetFromJsonAsync<JsonElement>($"/api/sites/{siteId}");
        Assert.False(site.GetProperty("attributes").TryGetProperty("cost_center", out _));
    }
}
