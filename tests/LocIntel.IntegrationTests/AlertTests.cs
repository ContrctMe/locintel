using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Alerts ride the IncidentReported event (high severity only) and bulletin
/// issuance; both are scope-filtered with optional subtree targeting, and
/// read/acknowledge state is per user.
/// </summary>
public class AlertTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task High_severity_incidents_become_alerts_and_reads_are_per_user()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var rootId = await RootAsync(owner);
        var siteId = await SiteAsync(owner, rootId, "Alert Store");
        var critical = await IncidentAsync(
            owner,
            siteId,
            "Armed robbery at register",
            "Robbery",
            "Critical"
        );
        await IncidentAsync(owner, siteId, "Candy bar shoplift", "Theft", "Low");

        JsonElement feed = default;
        var found = false;
        for (var i = 0; i < 50 && !found; i++)
        {
            await Task.Delay(100);
            feed = await owner.GetFromJsonAsync<JsonElement>("/api/alerts");
            found = feed.GetProperty("items")
                .EnumerateArray()
                .Any(a =>
                    a.TryGetProperty("incidentId", out var inc)
                    && inc.ValueKind == JsonValueKind.String
                    && inc.GetGuid() == critical
                );
        }
        Assert.True(found, "the critical incident should have produced an alert");
        var alert = feed.GetProperty("items")
            .EnumerateArray()
            .Single(a =>
                a.GetProperty("incidentId").ValueKind == JsonValueKind.String
                && a.GetProperty("incidentId").GetGuid() == critical
            );
        Assert.Equal("Critical", alert.GetProperty("severity").GetString());
        Assert.Equal("HighSeverityIncident", alert.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, alert.GetProperty("readAt").ValueKind);
        // the low one never became an alert
        Assert.DoesNotContain(
            feed.GetProperty("items").EnumerateArray(),
            a => a.GetProperty("body").GetString() == "Candy bar shoplift"
        );

        var alertId = alert.GetProperty("id").GetGuid();
        var before = await owner.GetFromJsonAsync<JsonElement>("/api/alerts/summary");
        (await owner.PostAsync($"/api/alerts/{alertId}/read", null)).EnsureSuccessStatusCode();
        var after = await owner.GetFromJsonAsync<JsonElement>("/api/alerts/summary");
        Assert.Equal(
            before.GetProperty("unread").GetInt32() - 1,
            after.GetProperty("unread").GetInt32()
        );
        var unreadOnly = await owner.GetFromJsonAsync<JsonElement>("/api/alerts?unreadOnly=true");
        Assert.DoesNotContain(
            unreadOnly.GetProperty("items").EnumerateArray(),
            a => a.GetProperty("id").GetGuid() == alertId
        );

        // tiers and tenants
        var outsider = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.PostAsync($"/api/alerts/{alertId}/read", null)).StatusCode
        );
        var theirs = await outsider.GetFromJsonAsync<JsonElement>("/api/alerts");
        Assert.DoesNotContain(
            theirs.GetProperty("items").EnumerateArray(),
            a => a.GetProperty("id").GetGuid() == alertId
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fixture.GuestClient().GetAsync("/api/alerts")).StatusCode
        );
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await viewer.GetAsync("/api/alerts/summary")).StatusCode
        );
    }

    [Fact]
    public async Task Bulletins_target_subtrees_and_track_acknowledgements()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var rootId = await RootAsync(owner);
        var east = await NodeAsync(owner, rootId, "Bulletin East");
        var west = await NodeAsync(owner, rootId, "Bulletin West");
        var eastSite = await SiteAsync(owner, east.id, "Bulletin East Store");

        var role = await owner.PostAsJsonAsync(
            "/api/roles",
            new
            {
                name = "East Staff",
                grants = new[] { new { domain = "alerts", action = "read" } },
            }
        );
        role.EnsureSuccessStatusCode();
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var staffId = await fixture.CreateMemberAsync("east-staff@locintel.local", fixture.OrgA);
        (
            await owner.PostAsJsonAsync(
                $"/api/roles/{roleId}/assign",
                new { userId = staffId, scopePath = east.path }
            )
        ).EnsureSuccessStatusCode();
        var staff = await fixture.LoginAsync("east-staff@locintel.local");

        var orgWide = await owner.PostAsJsonAsync(
            "/api/bulletins",
            new
            {
                kind = "Bolo",
                severity = "High",
                title = "Silver sedan ABC-123",
                body = "Seen leaving two stores after grab-and-runs. Do not approach; call it in.",
                expiresAt = DateTimeOffset.UtcNow.AddDays(7),
            }
        );
        Assert.True(orgWide.IsSuccessStatusCode, await orgWide.Content.ReadAsStringAsync());
        var orgWideId = (await orgWide.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var westOnly = await owner.PostAsJsonAsync(
            "/api/bulletins",
            new
            {
                kind = "Advisory",
                severity = "Medium",
                title = "West: counterfeit fifties",
                body = "Check the strip.",
                scopePath = west.path,
            }
        );
        westOnly.EnsureSuccessStatusCode();
        var westOnlyId = (await westOnly.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await owner.PostAsJsonAsync(
                    "/api/bulletins",
                    new
                    {
                        kind = "Bolo",
                        severity = "Low",
                        title = "Too long",
                        body = "x",
                        expiresAt = DateTimeOffset.UtcNow.AddDays(400),
                    }
                )
            ).StatusCode
        );

        // east staff see the org-wide one, not the west one
        var seen = await staff.GetFromJsonAsync<JsonElement>("/api/bulletins");
        var ids = seen.GetProperty("items")
            .EnumerateArray()
            .Select(b => b.GetProperty("id").GetGuid())
            .ToList();
        Assert.Contains(orgWideId, ids);
        Assert.DoesNotContain(westOnlyId, ids);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await staff.GetAsync($"/api/bulletins/{westOnlyId}")).StatusCode
        );
        // and no manage reach
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await staff.PostAsync($"/api/bulletins/{orgWideId}/withdraw", null)).StatusCode
        );
        // the issuance also landed in their feed
        var feed = await staff.GetFromJsonAsync<JsonElement>("/api/alerts");
        Assert.Contains(
            feed.GetProperty("items").EnumerateArray(),
            a =>
                a.GetProperty("bulletinId").ValueKind == JsonValueKind.String
                && a.GetProperty("bulletinId").GetGuid() == orgWideId
        );
        Assert.DoesNotContain(
            feed.GetProperty("items").EnumerateArray(),
            a =>
                a.GetProperty("bulletinId").ValueKind == JsonValueKind.String
                && a.GetProperty("bulletinId").GetGuid() == westOnlyId
        );

        // acknowledge at a site; the manager sees who did
        var summaryBefore = await staff.GetFromJsonAsync<JsonElement>("/api/alerts/summary");
        (
            await staff.PostAsJsonAsync(
                $"/api/bulletins/{orgWideId}/acknowledge",
                new { siteId = eastSite, note = "Briefed the opening crew" }
            )
        ).EnsureSuccessStatusCode();
        var summaryAfter = await staff.GetFromJsonAsync<JsonElement>("/api/alerts/summary");
        Assert.Equal(
            summaryBefore.GetProperty("unacknowledgedBulletins").GetInt32() - 1,
            summaryAfter.GetProperty("unacknowledgedBulletins").GetInt32()
        );
        var mine = await staff.GetFromJsonAsync<JsonElement>($"/api/bulletins/{orgWideId}");
        Assert.True(mine.GetProperty("bulletin").GetProperty("acknowledged").GetBoolean());
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("acknowledgements").ValueKind);
        var managers = await owner.GetFromJsonAsync<JsonElement>($"/api/bulletins/{orgWideId}");
        var ack = Assert.Single(managers.GetProperty("acknowledgements").EnumerateArray());
        Assert.Equal("east-staff@locintel.local", ack.GetProperty("user").GetString());
        Assert.Equal("Briefed the opening crew", ack.GetProperty("note").GetString());

        // withdraw: gone from the active list, still there with includeInactive
        (
            await owner.PostAsync($"/api/bulletins/{orgWideId}/withdraw", null)
        ).EnsureSuccessStatusCode();
        var active = await owner.GetFromJsonAsync<JsonElement>("/api/bulletins");
        Assert.DoesNotContain(
            active.GetProperty("items").EnumerateArray(),
            b => b.GetProperty("id").GetGuid() == orgWideId
        );
        var all = await owner.GetFromJsonAsync<JsonElement>("/api/bulletins?includeInactive=true");
        Assert.Contains(
            all.GetProperty("items").EnumerateArray(),
            b =>
                b.GetProperty("id").GetGuid() == orgWideId
                && b.GetProperty("status").GetString() == "Withdrawn"
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await staff.PostAsJsonAsync($"/api/bulletins/{orgWideId}/acknowledge", new { })
            ).StatusCode
        );

        // other tenants
        var outsider = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.GetAsync($"/api/bulletins/{westOnlyId}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.PostAsync($"/api/bulletins/{westOnlyId}/withdraw", null)).StatusCode
        );
    }

    private static async Task<Guid> RootAsync(HttpClient client)
    {
        var hierarchy = await client.GetAsync("/api/hierarchy");
        if (hierarchy.StatusCode == HttpStatusCode.OK)
            return (await hierarchy.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("nodes")
                .EnumerateArray()
                .First(n => n.GetProperty("depth").GetInt32() == 0)
                .GetProperty("id")
                .GetGuid();
        var created = await client.PostAsJsonAsync(
            "/api/hierarchy",
            new { name = "Org A", levels = new[] { "Region" } }
        );
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("rootNodeId")
            .GetGuid();
    }

    private static async Task<(Guid id, string path)> NodeAsync(
        HttpClient client,
        Guid parentId,
        string name
    )
    {
        var response = await client.PostAsJsonAsync("/api/hierarchy/nodes", new { parentId, name });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), body.GetProperty("path").GetString()!);
    }

    private static async Task<Guid> SiteAsync(HttpClient client, Guid nodeId, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId,
                name,
                timeZone = "Etc/UTC",
            }
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
    }

    private static async Task<Guid> IncidentAsync(
        HttpClient client,
        Guid siteId,
        string title,
        string category,
        string severity
    )
    {
        var response = await client.PostAsJsonAsync(
            "/api/incidents",
            new
            {
                siteId,
                category,
                severity,
                title,
                occurredAt = DateTimeOffset.UtcNow,
            }
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
    }
}
