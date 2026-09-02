using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Persons of interest: the fourth gate (need-to-know via scope, grant, or
/// the manage role), the always-on view audit, evidence before confirmation,
/// and mandatory retention with legal hold.
/// </summary>
public class EntityTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Need_to_know_gate_filters_by_scope_grant_and_role()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var rootId = await RootAsync(owner);
        var east = await NodeAsync(owner, rootId, "NTK East");
        var west = await NodeAsync(owner, rootId, "NTK West");
        var eastSite = await SiteAsync(owner, east.id, "NTK East Store");
        var westSite = await SiteAsync(owner, west.id, "NTK West Store");
        var eastIncident = await IncidentAsync(owner, eastSite, "East grab-and-run");
        var westIncident = await IncidentAsync(owner, westSite, "West grab-and-run");

        // two persons, one seen at each store; a vehicle with no evidence yet
        var westPerson = await EntityAsync(
            owner,
            new
            {
                kind = "Person",
                displayName = "Tall Hat",
                aliases = new[] { "Hat Guy", " " },
                descriptors = new Dictionary<string, string>
                {
                    ["Height"] = "6'2\"",
                    ["hair"] = "shaved",
                },
                summary = "Seen on West CCTV.",
            }
        );
        var eastPerson = await EntityAsync(
            owner,
            new { kind = "Person", displayName = "Red Jacket" }
        );
        var vehicle = await EntityAsync(
            owner,
            new { kind = "Vehicle", displayName = "Silver sedan ABC-123" }
        );
        await LinkAsync(owner, westPerson, westIncident, "Suspect");
        await LinkAsync(owner, eastPerson, eastIncident, "Suspect");

        // a scoped analyst: incidents + entities:read at the EAST subtree only
        var role = await owner.PostAsJsonAsync(
            "/api/roles",
            new
            {
                name = "East Analyst",
                grants = new[]
                {
                    new { domain = "incidents", action = "*" },
                    new { domain = "entities", action = "read" },
                },
            }
        );
        role.EnsureSuccessStatusCode();
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var analystId = await fixture.CreateMemberAsync(
            "east-analyst@locintel.local",
            fixture.OrgA
        );
        (
            await owner.PostAsJsonAsync(
                $"/api/roles/{roleId}/assign",
                new { userId = analystId, scopePath = east.path }
            )
        ).EnsureSuccessStatusCode();
        var analyst = await fixture.LoginAsync("east-analyst@locintel.local");

        // scope half of the gate: only the person linked to an East incident
        var visible = await analyst.GetFromJsonAsync<JsonElement>("/api/entities");
        var visibleIds = visible
            .GetProperty("items")
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .ToList();
        Assert.Contains(eastPerson, visibleIds);
        Assert.DoesNotContain(westPerson, visibleIds);
        Assert.DoesNotContain(vehicle, visibleIds);
        Assert.Equal(
            HttpStatusCode.OK,
            (await analyst.GetAsync($"/api/entities/{eastPerson}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await analyst.GetAsync($"/api/entities/{westPerson}")).StatusCode
        );
        // and no manage reach at all
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await analyst.PostAsJsonAsync(
                    "/api/entities",
                    new { kind = "Person", displayName = "Nope" }
                )
            ).StatusCode
        );

        // grant half of the gate: share the West person for a day, then revoke
        var grant = await owner.PostAsJsonAsync(
            $"/api/entities/{westPerson}/grants",
            new
            {
                userId = analystId,
                reason = "Same MO as East case",
                expiresAt = DateTimeOffset.UtcNow.AddDays(1),
            }
        );
        Assert.True(grant.IsSuccessStatusCode, await grant.Content.ReadAsStringAsync());
        var grantId = (await grant.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var shared = await analyst.GetFromJsonAsync<JsonElement>($"/api/entities/{westPerson}");
        Assert.Equal("Tall Hat", shared.GetProperty("displayName").GetString());
        Assert.Equal("6'2\"", shared.GetProperty("descriptors").GetProperty("height").GetString());
        Assert.Equal(
            new[] { "Hat Guy" },
            shared.GetProperty("aliases").EnumerateArray().Select(a => a.GetString()).ToArray()
        );
        // the analyst cannot see the West link (its incident is out of scope)
        // and never sees grants
        Assert.Empty(shared.GetProperty("links").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, shared.GetProperty("grants").ValueKind);
        (
            await owner.DeleteAsync($"/api/entities/{westPerson}/grants/{grantId}")
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await analyst.GetAsync($"/api/entities/{westPerson}")).StatusCode
        );

        // the manager sees the whole graph, links and grants included
        var full = await owner.GetFromJsonAsync<JsonElement>($"/api/entities/{westPerson}");
        var link = Assert.Single(full.GetProperty("links").EnumerateArray());
        Assert.Equal("West grab-and-run", link.GetProperty("incidentTitle").GetString());
        Assert.Equal(JsonValueKind.Array, full.GetProperty("grants").ValueKind);

        // search by alias, filter by incident
        var byAlias = await owner.GetFromJsonAsync<JsonElement>("/api/entities?q=hat%20guy");
        Assert.Contains(
            byAlias.GetProperty("items").EnumerateArray(),
            e => e.GetProperty("id").GetGuid() == westPerson
        );
        var byIncident = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/entities?incidentId={eastIncident}"
        );
        Assert.Equal(eastPerson, byIncident.GetProperty("items")[0].GetProperty("id").GetGuid());

        // evidence before confirmation
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await owner.PostAsJsonAsync(
                    $"/api/entities/{vehicle}/status",
                    new { status = "Confirmed" }
                )
            ).StatusCode
        );
        await LinkAsync(owner, vehicle, westIncident, "VehicleUsed");
        (
            await owner.PostAsJsonAsync(
                $"/api/entities/{vehicle}/status",
                new { status = "Confirmed" }
            )
        ).EnsureSuccessStatusCode();

        // other tiers and tenants
        var outsider = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.GetAsync($"/api/entities/{eastPerson}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await outsider.PostAsJsonAsync(
                    $"/api/entities/{eastPerson}/links",
                    new { incidentId = eastIncident, role = "Suspect" }
                )
            ).StatusCode
        );
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await viewer.GetAsync("/api/entities")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fixture.GuestClient().GetAsync($"/api/entities/{eastPerson}")).StatusCode
        );

        // the fifth audit kind: the analyst's read of the East person is on record
        List<LocIntel.Modules.Audit.Data.DomainLogEntry> views = [];
        for (var i = 0; i < 50 && views.Count == 0; i++)
        {
            await Task.Delay(100);
            views = await fixture.QueryAudit(db =>
                db.DomainEvents.IgnoreQueryFilters()
                    .Where(a =>
                        a.OrgId == fixture.OrgA.Value
                        && a.EventName == "entity.viewed"
                        && a.ActorId == analystId
                    )
            );
            views = views.Where(v => v.Payload.Contains(eastPerson.ToString())).ToList();
        }
        Assert.NotEmpty(views);
    }

    [Fact]
    public async Task Retention_sweep_trashes_expired_records_unless_held()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await owner.PostAsJsonAsync(
                    "/api/entities",
                    new
                    {
                        kind = "Person",
                        displayName = "Forever",
                        expiresAt = DateTimeOffset.UtcNow.AddYears(5),
                    }
                )
            ).StatusCode
        );
        var expired = await EntityAsync(
            owner,
            new
            {
                kind = "Person",
                displayName = "Stale Record",
                expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            }
        );
        var held = await EntityAsync(
            owner,
            new
            {
                kind = "Person",
                displayName = "Held Record",
                expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            }
        );
        (
            await owner.PostAsJsonAsync($"/api/entities/{held}/hold", new { hold = true })
        ).EnsureSuccessStatusCode();
        var fresh = await EntityAsync(owner, new { kind = "Group", displayName = "Fresh Crew" });

        (await owner.PostAsync("/api/entities/retention/sweep", null)).EnsureSuccessStatusCode();
        JsonElement trash = default;
        var trashed = new List<Guid>();
        for (var i = 0; i < 50 && !trashed.Contains(expired); i++)
        {
            await Task.Delay(100);
            trash = await owner.GetFromJsonAsync<JsonElement>("/api/entities?trash=true");
            trashed = trash
                .GetProperty("items")
                .EnumerateArray()
                .Select(e => e.GetProperty("id").GetGuid())
                .ToList();
        }
        Assert.Contains(expired, trashed);
        Assert.DoesNotContain(held, trashed);
        Assert.DoesNotContain(fresh, trashed);
        // held: still live, still refuses deletion
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/entities/{held}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await owner.DeleteAsync($"/api/entities/{held}")).StatusCode
        );

        // restore gives an expired record a fresh review window
        var restored = await owner.PostAsync($"/api/entities/{expired}/restore", null);
        restored.EnsureSuccessStatusCode();
        var state = await restored.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(state.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("deletedAt").ValueKind);
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

    private static async Task<Guid> IncidentAsync(HttpClient client, Guid siteId, string title)
    {
        var response = await client.PostAsJsonAsync(
            "/api/incidents",
            new
            {
                siteId,
                category = "Theft",
                severity = "Medium",
                title,
                occurredAt = DateTimeOffset.UtcNow,
            }
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
    }

    private static async Task<Guid> EntityAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/entities", body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
    }

    private static async Task LinkAsync(
        HttpClient client,
        Guid entityId,
        Guid incidentId,
        string role
    )
    {
        var response = await client.PostAsJsonAsync(
            $"/api/entities/{entityId}/links",
            new { incidentId, role }
        );
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }
}
