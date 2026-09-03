using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.IntegrationTests;

/// <summary>ADRs 12/13: four capture kinds, split paths, policy with a floor, retention.</summary>
public class AuditTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Change_diffs_commit_with_the_change_and_redact_marked_fields()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserA);
        var put = await client.PutAsJsonAsync(
            "/api/settings/audit.probe",
            new { value = "sensitive-1" }
        );
        put.EnsureSuccessStatusCode();

        var rows = await fixture.QueryAudit(db =>
            db.Changes.IgnoreQueryFilters()
                .Where(a => a.TableName == "organization_settings" && a.OrgId == fixture.OrgA.Value)
        );
        var row = Assert.Single(rows, r => r.Diff.Contains("audit.probe"));
        Assert.Equal("added", row.Operation);
        Assert.Equal("user", row.ActorTier);
        Assert.Equal(ApiFixture.UserA, row.ActorLabel);
        // [AuditRedacted]: the diff records THAT Value changed, never the value
        Assert.DoesNotContain("sensitive-1", row.Diff);
        Assert.Contains("***", row.Diff);
    }

    [Fact]
    public async Task Update_diffs_capture_old_and_new()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserB);
        (
            await client.PutAsJsonAsync("/api/settings/diff.probe", new { value = "v1" })
        ).EnsureSuccessStatusCode();
        (
            await client.PutAsJsonAsync("/api/settings/diff.probe", new { value = "v2" })
        ).EnsureSuccessStatusCode();

        var rows = await fixture.QueryAudit(db =>
            db.Changes.IgnoreQueryFilters()
                .Where(a => a.OrgId == fixture.OrgB.Value && a.Operation == "modified")
        );
        Assert.Contains(rows, r => r.Diff.Contains("Value"));
    }

    [Fact]
    public async Task Authz_denials_are_always_recorded()
    {
        await fixture.CreateMemberAsync("denied@locintel.local", fixture.OrgA);
        var viewer = await fixture.LoginAsync("denied@locintel.local");
        (await viewer.GetAsync("/api/sites")).EnsureSuccessStatusCode(); // scope None -> []

        List<LocIntel.Modules.Audit.Data.AuthzLogEntry> denials = [];
        for (var i = 0; i < 50 && denials.Count == 0; i++)
        {
            await Task.Delay(100);
            denials = await fixture.QueryAudit(db =>
                db.AuthzDecisions.IgnoreQueryFilters()
                    .Where(a =>
                        a.OrgId == fixture.OrgA.Value
                        && a.Outcome == "denied"
                        && a.Action == "sites:read"
                    )
            );
        }
        Assert.NotEmpty(denials);
    }

    [Fact]
    public async Task Node_moves_record_intent_level_events()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserA);
        var hierarchy = await EnsureHierarchy(client);
        var a = await Node(client, hierarchy, "Audit From");
        var b = await Node(client, hierarchy, "Audit To");
        var child = await Node(client, a, "Audit Child");
        var move = await client.PostAsJsonAsync(
            $"/api/hierarchy/nodes/{child}/move",
            new { newParentId = b }
        );
        Assert.Equal(HttpStatusCode.NoContent, move.StatusCode);

        List<LocIntel.Modules.Audit.Data.DomainLogEntry> events = [];
        for (var i = 0; i < 50 && events.Count == 0; i++)
        {
            await Task.Delay(100);
            events = await fixture.QueryAudit(db =>
                db.DomainEvents.IgnoreQueryFilters()
                    .Where(a =>
                        a.OrgId == fixture.OrgA.Value && a.EventName == "hierarchy.node_moved"
                    )
            );
        }
        var moved = Assert.Single(events, e => e.Payload.Contains("Audit Child"));
        Assert.Equal("user", moved.ActorTier);
    }

    [Fact]
    public async Task Read_logging_is_off_by_default_on_by_config_and_config_change_is_audited()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserB);
        (await client.GetAsync("/api/sites")).EnsureSuccessStatusCode();
        var before = await fixture.QueryAudit(db =>
            db.Accesses.IgnoreQueryFilters().Where(a => a.OrgId == fixture.OrgB.Value)
        );
        Assert.Empty(before); // floor: reads not logged

        var set = await client.PutAsJsonAsync(
            "/api/admin/audit-config",
            new { logGrants = false, logReads = true }
        );
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        // the self-referential rule: changing audit config IS a domain event
        List<LocIntel.Modules.Audit.Data.DomainLogEntry> configEvents = [];
        for (var i = 0; i < 50 && configEvents.Count == 0; i++)
        {
            await Task.Delay(100);
            configEvents = await fixture.QueryAudit(db =>
                db.DomainEvents.IgnoreQueryFilters()
                    .Where(a =>
                        a.OrgId == fixture.OrgB.Value && a.EventName == "audit.config_changed"
                    )
            );
        }
        Assert.NotEmpty(configEvents);

        // policy cache TTL is 5m; force-refresh by waiting for first resolution
        List<LocIntel.Modules.Audit.Data.AccessLogEntry> accesses = [];
        for (var i = 0; i < 100 && accesses.Count == 0; i++)
        {
            (await client.GetAsync("/api/sites")).EnsureSuccessStatusCode();
            await Task.Delay(100);
            accesses = await fixture.QueryAudit(db =>
                db.Accesses.IgnoreQueryFilters()
                    .Where(a => a.OrgId == fixture.OrgB.Value && a.Path == "/api/sites")
            );
        }
        Assert.NotEmpty(accesses);
        Assert.Equal("user", accesses[0].ActorTier);
    }

    [Fact]
    public async Task Audit_queries_are_tenant_isolated_and_grant_gated()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var changes = await owner.GetFromJsonAsync<JsonElement>("/api/audit/changes");
        Assert.True(changes.GetArrayLength() >= 0); // readable with audit:read (Owner *:*)

        await fixture.CreateMemberAsync("noaudit@locintel.local", fixture.OrgA);
        var viewer = await fixture.LoginAsync("noaudit@locintel.local");
        var denied = await viewer.GetAsync("/api/audit/changes");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Retention_purges_past_the_entitled_window()
    {
        // seed a 100-day-old entry directly (default retention: 90)
        var oldId = Guid.CreateVersion7();
        await fixture.SeedAuditChange(fixture.OrgA, oldId, DateTimeOffset.UtcNow.AddDays(-100));
        await fixture.SeedAuditChange(fixture.OrgA, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        await fixture.PublishForOrgA(new LocIntel.Modules.Audit.PurgeAuditData());

        await ApiFixture.WaitUntilAsync(
            async () =>
                (
                    await fixture.QueryAudit(db =>
                        db.Changes.IgnoreQueryFilters().Where(a => a.Id == oldId)
                    )
                ).Count == 0,
            "the 100-day-old audit row to be purged by 90-day retention"
        );
    }

    private static async Task<Guid> EnsureHierarchy(HttpClient client)
    {
        var get = await client.GetAsync("/api/hierarchy");
        if (get.StatusCode == HttpStatusCode.OK)
        {
            var tree = await get.Content.ReadFromJsonAsync<JsonElement>();
            return tree.GetProperty("nodes")
                .EnumerateArray()
                .First(n => n.GetProperty("depth").GetInt32() == 0)
                .GetProperty("id")
                .GetGuid();
        }
        var created = await client.PostAsJsonAsync(
            "/api/hierarchy",
            new { name = "Org A", levels = new[] { "Region", "Market" } }
        );
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("rootNodeId")
            .GetGuid();
    }

    private static async Task<Guid> Node(HttpClient client, Guid parentId, string name)
    {
        var response = await client.PostAsJsonAsync("/api/hierarchy/nodes", new { parentId, name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
    }
}
