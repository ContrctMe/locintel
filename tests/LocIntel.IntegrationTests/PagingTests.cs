using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Fleet-scale lists (UX gap: server paging/search): sites, members, and
/// files page with { items, total, nextOffset } and search server-side -
/// scope filters FIRST, then search, then the page.
/// </summary>
public class PagingTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Sites_page_and_search_server_side()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var created = await owner.PostAsJsonAsync(
            "/api/hierarchy",
            new { name = "Org A", levels = new[] { "Region" } }
        );
        created.EnsureSuccessStatusCode();
        var rootId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("rootNodeId")
            .GetGuid();
        foreach (var name in new[] { "Alpha Depot", "Beta Depot", "Gamma Store" })
            (
                await owner.PostAsJsonAsync(
                    "/api/sites",
                    new
                    {
                        nodeId = rootId,
                        name,
                        timeZone = "Etc/UTC",
                    }
                )
            ).EnsureSuccessStatusCode();

        // page 1 of 2, name-ordered, envelope carries the arithmetic
        var page1 = await owner.GetFromJsonAsync<JsonElement>("/api/sites?limit=2");
        Assert.Equal(3, page1.GetProperty("total").GetInt32());
        Assert.Equal(2, page1.GetProperty("items").GetArrayLength());
        Assert.Equal("Alpha Depot", page1.GetProperty("items")[0].GetProperty("name").GetString());
        var next = page1.GetProperty("nextOffset").GetInt32();
        Assert.Equal(2, next);

        var page2 = await owner.GetFromJsonAsync<JsonElement>($"/api/sites?limit=2&offset={next}");
        Assert.Equal(1, page2.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, page2.GetProperty("nextOffset").ValueKind);

        // search narrows total AND items, case-insensitively
        var depots = await owner.GetFromJsonAsync<JsonElement>("/api/sites?q=depot");
        Assert.Equal(2, depots.GetProperty("total").GetInt32());
        Assert.All(
            depots.GetProperty("items").EnumerateArray(),
            s => Assert.Contains("Depot", s.GetProperty("name").GetString())
        );
    }

    [Fact]
    public async Task Members_and_files_share_the_envelope()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var members = await owner.GetFromJsonAsync<JsonElement>("/api/members?q=user-a");
        Assert.True(members.GetProperty("total").GetInt32() >= 1);
        Assert.All(
            members.GetProperty("items").EnumerateArray(),
            m => Assert.Contains("user-a", m.GetProperty("email").GetString())
        );

        var files = await owner.GetFromJsonAsync<JsonElement>("/api/files?limit=5");
        Assert.Equal(JsonValueKind.Array, files.GetProperty("items").ValueKind);
        Assert.True(files.GetProperty("total").GetInt32() >= 0);
    }

    [Fact]
    public async Task Search_respects_scope_before_it_searches()
    {
        // org B searching for org A's site by name finds NOTHING - scope is
        // not a post-filter on search results
        var other = await fixture.LoginAsync(ApiFixture.UserB);
        var result = await other.GetFromJsonAsync<JsonElement>("/api/sites?q=Alpha");
        Assert.Equal(0, result.GetProperty("total").GetInt32());
        Assert.Equal(0, result.GetProperty("items").GetArrayLength());
    }
}
