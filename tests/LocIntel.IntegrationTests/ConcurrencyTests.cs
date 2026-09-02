using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Optimistic concurrency on the co-edited aggregate (operability item 5):
/// the client echoes the version it loaded; the stale editor gets a 409 and
/// a reload, never a silent clobber. Postgres xmin is the token - no
/// application column, no clock, nothing to maintain.
/// </summary>
public class ConcurrencyTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Stale_site_edit_conflicts_instead_of_clobbering()
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
                name = "Contended",
                timeZone = "Etc/UTC",
            }
        );
        created.EnsureSuccessStatusCode();
        var site = await created.Content.ReadFromJsonAsync<JsonElement>();
        var siteId = site.GetProperty("id").GetGuid();
        var loadedVersion = site.GetProperty("version").GetUInt32();

        // editor B saves first (same loaded version - the race)
        (
            await owner.PostAsJsonAsync(
                $"/api/sites/{siteId}",
                new { name = "Saved By B", version = loadedVersion }
            )
        ).EnsureSuccessStatusCode();

        // editor A, still holding the old version: conflict, not clobber
        var stale = await owner.PostAsJsonAsync(
            $"/api/sites/{siteId}",
            new { name = "Clobbered By A", version = loadedVersion }
        );
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var current = await owner.GetFromJsonAsync<JsonElement>($"/api/sites/{siteId}");
        Assert.Equal("Saved By B", current.GetProperty("name").GetString());
        Assert.NotEqual(loadedVersion, current.GetProperty("version").GetUInt32());

        // reload-and-retry succeeds with the fresh version
        (
            await owner.PostAsJsonAsync(
                $"/api/sites/{siteId}",
                new
                {
                    name = "A After Reload",
                    version = current.GetProperty("version").GetUInt32(),
                }
            )
        ).EnsureSuccessStatusCode();

        // a versionless update still works: older clients keep last-write-wins
        (
            await owner.PostAsJsonAsync($"/api/sites/{siteId}", new { name = "No Version" })
        ).EnsureSuccessStatusCode();
    }
}
