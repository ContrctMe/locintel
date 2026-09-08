using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.IntegrationTests;

public class HierarchyAndTimeTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> PostJson(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.True(
            response.IsSuccessStatusCode,
            $"{url} -> {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}"
        );
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<(HttpClient client, Guid rootId)> SetupHierarchy()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserA);
        var get = await client.GetAsync("/api/hierarchy");
        if (get.StatusCode == HttpStatusCode.OK)
        {
            var existing = await get.Content.ReadFromJsonAsync<JsonElement>();
            var root = existing
                .GetProperty("nodes")
                .EnumerateArray()
                .First(n => n.GetProperty("depth").GetInt32() == 0);
            return (client, root.GetProperty("id").GetGuid());
        }
        var created = await PostJson(
            client,
            "/api/hierarchy",
            new { name = "Org A", levels = new[] { "Region", "Market" } }
        );
        return (client, created.GetProperty("rootNodeId").GetGuid());
    }

    [Fact]
    public async Task Hierarchy_depth_is_capped_by_level_definitions()
    {
        var (client, rootId) = await SetupHierarchy();
        var region = await PostJson(
            client,
            "/api/hierarchy/nodes",
            new { parentId = rootId, name = "Northeast" }
        );
        var market = await PostJson(
            client,
            "/api/hierarchy/nodes",
            new { parentId = region.GetProperty("id").GetGuid(), name = "NYC Metro" }
        );

        // levels = [Region, Market] -> depth 3 below root must fail
        var tooDeep = await client.PostAsJsonAsync(
            "/api/hierarchy/nodes",
            new { parentId = market.GetProperty("id").GetGuid(), name = "Too Deep" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, tooDeep.StatusCode);
    }

    [Fact]
    public async Task Moving_a_node_rewrites_subtree_and_site_paths()
    {
        var (client, rootId) = await SetupHierarchy();
        var east = await PostJson(
            client,
            "/api/hierarchy/nodes",
            new { parentId = rootId, name = "East" }
        );
        var west = await PostJson(
            client,
            "/api/hierarchy/nodes",
            new { parentId = rootId, name = "West" }
        );
        var market = await PostJson(
            client,
            "/api/hierarchy/nodes",
            new { parentId = east.GetProperty("id").GetGuid(), name = "Boston" }
        );
        var site = await PostJson(
            client,
            "/api/sites",
            new
            {
                nodeId = market.GetProperty("id").GetGuid(),
                name = "Boston Store",
                timeZone = "America/New_York",
            }
        );
        var oldPath = site.GetProperty("path").GetString()!;
        Assert.StartsWith(east.GetProperty("path").GetString()!, oldPath);

        // move Boston market from East to West
        var move = await client.PostAsJsonAsync(
            $"/api/hierarchy/nodes/{market.GetProperty("id").GetGuid()}/move",
            new { newParentId = west.GetProperty("id").GetGuid() }
        );
        Assert.Equal(HttpStatusCode.NoContent, move.StatusCode);

        var moved = await client.GetFromJsonAsync<JsonElement>(
            $"/api/sites/{site.GetProperty("id").GetGuid()}"
        );
        Assert.StartsWith(
            west.GetProperty("path").GetString()!,
            moved.GetProperty("path").GetString()!
        );

        // subtree filter follows the new location
        var underWest = await ApiFixture.GetItemsAsync(
            client,
            $"/api/sites?under={west.GetProperty("id").GetGuid()}"
        );
        Assert.Contains(
            underWest.EnumerateArray(),
            s => s.GetProperty("name").GetString() == "Boston Store"
        );
    }

    [Fact]
    public async Task Cannot_move_node_under_its_own_subtree()
    {
        var (client, rootId) = await SetupHierarchy();
        var a = await PostJson(
            client,
            "/api/hierarchy/nodes",
            new { parentId = rootId, name = "Cycle A" }
        );
        var b = await PostJson(
            client,
            "/api/hierarchy/nodes",
            new { parentId = a.GetProperty("id").GetGuid(), name = "Cycle B" }
        );
        var move = await client.PostAsJsonAsync(
            $"/api/hierarchy/nodes/{a.GetProperty("id").GetGuid()}/move",
            new { newParentId = b.GetProperty("id").GetGuid() }
        );
        Assert.Equal(HttpStatusCode.BadRequest, move.StatusCode);
    }

    [Fact]
    public async Task Schedules_materialize_dst_correct_occurrences()
    {
        var (client, rootId) = await SetupHierarchy();
        var site = await PostJson(
            client,
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "DST Store",
                timeZone = "America/New_York",
            }
        );
        var siteId = site.GetProperty("id").GetGuid();

        await PostJson(
            client,
            $"/api/sites/{siteId}/schedules",
            new
            {
                name = "Weekday hours",
                rrule = "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR",
                anchorDate = "2026-01-05",
                opens = "09:00",
                closes = "17:00",
            }
        );

        var windows = await PollWindows(siteId, minimum: 50);
        // EDT (Oct 2026): 9am local = 13:00Z. EST (Dec 2026): 9am local = 14:00Z.
        var october = windows.Where(w => w.start.Month == 10).ToList();
        var december = windows.Where(w => w.start.Month == 12).ToList();
        Assert.NotEmpty(october);
        Assert.NotEmpty(december);
        Assert.All(october, w => Assert.Equal(13, w.start.Hour));
        Assert.All(december, w => Assert.Equal(14, w.start.Hour));
    }

    [Fact]
    public async Task Timezone_change_rebuilds_the_projection()
    {
        var (client, rootId) = await SetupHierarchy();
        var site = await PostJson(
            client,
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Moving Store",
                timeZone = "America/New_York",
            }
        );
        var siteId = site.GetProperty("id").GetGuid();
        await PostJson(
            client,
            $"/api/sites/{siteId}/schedules",
            new
            {
                name = "Hours",
                rrule = "FREQ=DAILY",
                anchorDate = "2026-01-01",
                opens = "09:00",
                closes = "17:00",
            }
        );
        var before = await PollWindows(siteId, minimum: 10);

        // the rebuild trigger everyone forgets (ADR 28)
        var patch = await client.PostAsJsonAsync(
            $"/api/sites/{siteId}",
            new { timeZone = "America/Los_Angeles" }
        );
        patch.EnsureSuccessStatusCode();

        List<(DateTimeOffset start, DateTimeOffset end)> after = [];
        await ApiFixture.WaitUntilAsync(
            async () =>
                (after = await PollWindows(siteId, minimum: 0)).Count > 0
                && after.Any(w => w.start == before[0].start.AddHours(3)),
            "occurrences to be re-expanded in the new time zone"
        );
        // same local 9am is 3 hours later in UTC on the west coast. Match by
        // value, not index: expansion-from can include one extra occurrence on
        // the horizon-start's LOCAL date, and that date differs by zone.
        Assert.Contains(after, w => w.start == before[0].start.AddHours(3));
    }

    [Fact]
    public async Task Open_now_is_an_indexed_query_over_the_projection()
    {
        var (client, rootId) = await SetupHierarchy();
        var site = await PostJson(
            client,
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Always Open",
                timeZone = "Etc/UTC",
            }
        );
        var siteId = site.GetProperty("id").GetGuid();
        await PostJson(
            client,
            $"/api/sites/{siteId}/schedules",
            new
            {
                name = "24-ish",
                rrule = "FREQ=DAILY",
                anchorDate = "2026-01-01",
                opens = "00:00",
                closes = "23:59",
            }
        );
        await PollWindows(siteId, minimum: 10);

        var open = await client.GetFromJsonAsync<JsonElement>("/api/sites/open-now");
        Assert.Contains(open.EnumerateArray(), s => s.GetProperty("id").GetGuid() == siteId);
    }

    [Fact]
    public async Task Horizon_roll_fans_out_and_restores_the_projection()
    {
        var (client, rootId) = await SetupHierarchy();
        var site = await PostJson(
            client,
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Rolled Store",
                timeZone = "Etc/UTC",
            }
        );
        var siteId = site.GetProperty("id").GetGuid();
        await PostJson(
            client,
            $"/api/sites/{siteId}/schedules",
            new
            {
                name = "Hours",
                rrule = "FREQ=DAILY",
                anchorDate = "2026-01-01",
                opens = "08:00",
                closes = "18:00",
            }
        );
        await PollWindows(siteId, minimum: 10);

        // wipe the projection, then run the enumerator's per-org message
        await fixture.DeleteWindows(siteId);
        Assert.Empty(await fixture.QueryWindows(siteId));
        await fixture.PublishForOrgA(new LocIntel.Modules.Tenancy.Sites.RollOccurrenceHorizons());

        var restored = await PollWindows(siteId, minimum: 10);
        Assert.NotEmpty(restored);
    }

    [Fact]
    public async Task Schedules_are_listable_deletable_and_windows_preview_works()
    {
        var (client, rootId) = await SetupHierarchy();
        var site = await PostJson(
            client,
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Hours Store",
                timeZone = "Etc/UTC",
            }
        );
        var siteId = site.GetProperty("id").GetGuid();
        var schedule = await PostJson(
            client,
            $"/api/sites/{siteId}/schedules",
            new
            {
                name = "Weekdays",
                rrule = "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR",
                anchorDate = "2026-01-05",
                opens = "09:00",
                closes = "17:00",
            }
        );
        await PollWindows(siteId, minimum: 10);

        var listed = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/sites/{siteId}/schedules"
        );
        var row = Assert.Single(listed.EnumerateArray());
        Assert.Equal("Weekdays", row.GetProperty("name").GetString());

        var preview = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/sites/{siteId}/windows?days=14"
        );
        Assert.True(preview.GetArrayLength() > 0, "windows preview empty");

        // deleting the rule empties the projection (rebuild trigger)
        var delete = await client.DeleteAsync(
            $"/api/sites/{siteId}/schedules/{row.GetProperty("id").GetGuid()}"
        );
        delete.EnsureSuccessStatusCode();
        await ApiFixture.WaitUntilAsync(
            async () => (await fixture.QueryWindows(siteId)).Count == 0,
            "the occurrence windows to be removed with their schedule"
        );
    }

    private async Task<List<(DateTimeOffset start, DateTimeOffset end)>> PollWindows(
        Guid siteId,
        int minimum
    )
    {
        List<(DateTimeOffset start, DateTimeOffset end)> polled = [];
        await ApiFixture.WaitUntilAsync(
            async () => (polled = await fixture.QueryWindows(siteId)).Count >= Math.Max(minimum, 1),
            $"the occurrence projection to reach at least {Math.Max(minimum, 1)} rows",
            diagnostics: fixture.DeadLetterSummary
        );
        return polled;
    }

    [Fact]
    public async Task Node_rename_is_display_only_and_paths_hold()
    {
        var (client, rootId) = await SetupHierarchy();
        var node = await PostJson(
            client,
            "/api/hierarchy/nodes",
            new { parentId = rootId, name = "Renameable" }
        );
        var nodeId = node.GetProperty("id").GetGuid();
        var pathBefore = node.GetProperty("path").GetString();

        var renamed = await client.PutAsJsonAsync(
            $"/api/hierarchy/nodes/{nodeId}",
            new { name = "Renamed Region" }
        );
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);

        var tree = await client.GetFromJsonAsync<JsonElement>("/api/hierarchy");
        var after = tree.GetProperty("nodes")
            .EnumerateArray()
            .First(n => n.GetProperty("id").GetGuid() == nodeId);
        Assert.Equal("Renamed Region", after.GetProperty("name").GetString());
        // paths are id-labeled: display names never leak into structure
        Assert.Equal(pathBefore, after.GetProperty("path").GetString());
    }

    [Fact]
    public async Task Node_delete_takes_only_empty_leaves()
    {
        var (client, rootId) = await SetupHierarchy();

        // the root is untouchable
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.DeleteAsync($"/api/hierarchy/nodes/{rootId}")).StatusCode
        );

        // a node with children refuses
        var parent = await PostJson(
            client,
            "/api/hierarchy/nodes",
            new { parentId = rootId, name = "DeleteParent" }
        );
        var parentNodeId = parent.GetProperty("id").GetGuid();
        var child = await PostJson(
            client,
            "/api/hierarchy/nodes",
            new { parentId = parentNodeId, name = "DeleteChild" }
        );
        var childId = child.GetProperty("id").GetGuid();
        var withChildren = await client.DeleteAsync($"/api/hierarchy/nodes/{parentNodeId}");
        Assert.Equal(HttpStatusCode.Conflict, withChildren.StatusCode);

        // a node with sites refuses - sites close, they never orphan (ADR 25)
        var site = await PostJson(
            client,
            "/api/sites",
            new
            {
                nodeId = childId,
                name = "Anchored Site",
                timeZone = "Etc/UTC",
            }
        );
        var withSites = await client.DeleteAsync($"/api/hierarchy/nodes/{childId}");
        Assert.Equal(HttpStatusCode.Conflict, withSites.StatusCode);
        Assert.Contains(
            "1 site(s)",
            (await withSites.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("error")
                .GetString()
        );

        // an empty leaf deletes; the tree no longer knows it
        var leaf = await PostJson(
            client,
            "/api/hierarchy/nodes",
            new { parentId = parentNodeId, name = "EmptyLeaf" }
        );
        var leafId = leaf.GetProperty("id").GetGuid();
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/hierarchy/nodes/{leafId}")).StatusCode
        );
        var tree = await client.GetFromJsonAsync<JsonElement>("/api/hierarchy");
        Assert.DoesNotContain(
            tree.GetProperty("nodes").EnumerateArray(),
            n => n.GetProperty("id").GetGuid() == leafId
        );
    }
}
