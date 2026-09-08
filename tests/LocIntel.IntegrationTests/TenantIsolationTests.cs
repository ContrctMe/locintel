using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The golden suite: every id-addressed endpoint is replayed as tenant B
/// against tenant A's ids and must 404 - never 200, never 403 (a 403 confirms
/// the resource exists). Clients authenticate through the real cookie flow.
/// Every new id-addressed endpoint gets a row here.
/// </summary>
public class TenantIsolationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Own_setting_is_readable()
    {
        var id = await fixture.SettingIdOf(fixture.OrgA, "brand.color");
        var client = await fixture.LoginAsync(ApiFixture.UserA);
        var response = await client.GetAsync($"/api/settings/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Other_tenants_setting_by_id_is_404_not_403()
    {
        var orgAsSettingId = await fixture.SettingIdOf(fixture.OrgA, "brand.color");
        var client = await fixture.LoginAsync(ApiFixture.UserB);
        var response = await client.GetAsync($"/api/settings/{orgAsSettingId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_never_contains_another_tenants_rows()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserB);
        var settings = await client.GetFromJsonAsync<List<SettingDto>>("/api/settings");
        var setting = Assert.Single(settings!, s => s.Key == "brand.color");
        Assert.Equal("#0A6E8A", setting.Value); // org B's value, never org A's
    }

    [Fact]
    public async Task Write_lands_in_own_tenant_only()
    {
        var clientB = await fixture.LoginAsync(ApiFixture.UserB);
        var put = await clientB.PutAsJsonAsync(
            "/api/settings/onboarding.step",
            new { value = "2" }
        );
        put.EnsureSuccessStatusCode();

        var clientA = await fixture.LoginAsync(ApiFixture.UserA);
        var orgAList = await clientA.GetFromJsonAsync<List<SettingDto>>("/api/settings");
        Assert.DoesNotContain(orgAList!, s => s.Key == "onboarding.step");
    }

    [Fact]
    public async Task Guest_with_no_org_sees_no_rows_fail_closed()
    {
        var response = await fixture.GuestClient().GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sites_are_tenant_isolated()
    {
        var clientA = await fixture.LoginAsync(ApiFixture.UserA);
        var hierarchy = await clientA.GetAsync("/api/hierarchy");
        Guid rootId;
        if (hierarchy.StatusCode == HttpStatusCode.OK)
        {
            var tree = await hierarchy.Content.ReadFromJsonAsync<JsonElement>();
            rootId = tree.GetProperty("nodes")
                .EnumerateArray()
                .First(n => n.GetProperty("depth").GetInt32() == 0)
                .GetProperty("id")
                .GetGuid();
        }
        else
        {
            var created = await clientA.PostAsJsonAsync(
                "/api/hierarchy",
                new { name = "Org A", levels = new[] { "Region" } }
            );
            created.EnsureSuccessStatusCode();
            rootId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("rootNodeId")
                .GetGuid();
        }
        var site = await clientA.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Isolated Store",
                timeZone = "Etc/UTC",
            }
        );
        site.EnsureSuccessStatusCode();
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var clientB = await fixture.LoginAsync(ApiFixture.UserB);
        // id-addressed replay: 404, never 200, never 403
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.GetAsync($"/api/sites/{siteId}")).StatusCode
        );
        // list: empty of org A's rows
        var list = await ApiFixture.GetItemsAsync(clientB, "/api/sites");
        Assert.DoesNotContain(
            list.EnumerateArray(),
            s => s.GetProperty("name").GetString() == "Isolated Store"
        );
        // org B holds no hierarchy at all
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.GetAsync("/api/hierarchy")).StatusCode
        );
    }

    [Fact]
    public async Task Incidents_are_tenant_isolated()
    {
        var clientA = await fixture.LoginAsync(ApiFixture.UserA);
        var hierarchy = await clientA.GetAsync("/api/hierarchy");
        Guid rootId;
        if (hierarchy.StatusCode == HttpStatusCode.OK)
            rootId = (await hierarchy.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("nodes")
                .EnumerateArray()
                .First(n => n.GetProperty("depth").GetInt32() == 0)
                .GetProperty("id")
                .GetGuid();
        else
        {
            var created = await clientA.PostAsJsonAsync(
                "/api/hierarchy",
                new { name = "Org A", levels = new[] { "Region" } }
            );
            created.EnsureSuccessStatusCode();
            rootId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("rootNodeId")
                .GetGuid();
        }
        var site = await clientA.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Incident Store",
                timeZone = "Etc/UTC",
            }
        );
        site.EnsureSuccessStatusCode();
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var reported = await clientA.PostAsJsonAsync(
            "/api/incidents",
            new
            {
                siteId,
                category = "Theft",
                severity = "Low",
                title = "Isolated incident",
                occurredAt = DateTimeOffset.UtcNow,
            }
        );
        reported.EnsureSuccessStatusCode();
        var id = (await reported.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var clientB = await fixture.LoginAsync(ApiFixture.UserB);
        // every id-addressed replay: 404, never 200, never 403
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.GetAsync($"/api/incidents/{id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await clientB.PostAsJsonAsync($"/api/incidents/{id}/close", new { reason = "x" })
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await clientB.PostAsJsonAsync($"/api/incidents/{id}/hold", new { hold = true })
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.DeleteAsync($"/api/incidents/{id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.PostAsync($"/api/incidents/{id}/restore", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await clientB.PostAsJsonAsync(
                    $"/api/incidents/{id}/attachments",
                    new { fileId = Guid.NewGuid() }
                )
            ).StatusCode
        );
        var list = await clientB.GetFromJsonAsync<JsonElement>("/api/incidents");
        Assert.DoesNotContain(
            list.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == id
        );
    }

    [Fact]
    public async Task Entities_are_tenant_isolated()
    {
        var clientA = await fixture.LoginAsync(ApiFixture.UserA);
        var created = await clientA.PostAsJsonAsync(
            "/api/entities",
            new { kind = "Person", displayName = "Isolated Person" }
        );
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var clientB = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.GetAsync($"/api/entities/{id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await clientB.PostAsJsonAsync(
                    $"/api/entities/{id}/status",
                    new { status = "Cleared" }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await clientB.PostAsJsonAsync($"/api/entities/{id}/hold", new { hold = true })
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.DeleteAsync($"/api/entities/{id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.PostAsync($"/api/entities/{id}/restore", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await clientB.PostAsJsonAsync(
                    $"/api/entities/{id}/grants",
                    new
                    {
                        userId = Guid.NewGuid(),
                        reason = "x",
                        expiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    }
                )
            ).StatusCode
        );
        var list = await clientB.GetFromJsonAsync<JsonElement>("/api/entities");
        Assert.DoesNotContain(
            list.GetProperty("items").EnumerateArray(),
            e => e.GetProperty("id").GetGuid() == id
        );
    }

    [Fact]
    public async Task Cases_are_tenant_isolated()
    {
        var clientA = await fixture.LoginAsync(ApiFixture.UserA);
        var created = await clientA.PostAsJsonAsync("/api/cases", new { title = "Isolated case" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var clientB = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.GetAsync($"/api/cases/{id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.GetAsync($"/api/cases/{id}/custody")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.GetAsync($"/api/cases/{id}/package")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await clientB.PostAsJsonAsync(
                    $"/api/cases/{id}/close",
                    new { disposition = "Resolved" }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.PostAsJsonAsync($"/api/cases/{id}/hold", new { hold = true })).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.PostAsJsonAsync($"/api/cases/{id}/notes", new { body = "x" })).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await clientB.PostAsJsonAsync(
                    $"/api/cases/{id}/evidence",
                    new { fileId = Guid.NewGuid() }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.DeleteAsync($"/api/cases/{id}")).StatusCode
        );
        var list = await clientB.GetFromJsonAsync<JsonElement>("/api/cases");
        Assert.DoesNotContain(
            list.GetProperty("items").EnumerateArray(),
            c => c.GetProperty("id").GetGuid() == id
        );
    }

    [Fact]
    public async Task Marketplace_rows_are_sealed_to_their_two_parties()
    {
        // org B is the vendor here; a THIRD org (the platform org, via the
        // operator) must see neither side of the request
        var vendor = await fixture.LoginAsync(ApiFixture.UserB);
        (
            await vendor.PutAsJsonAsync(
                "/api/vendor/profile",
                new { name = "Isolation Vendor", categories = new[] { "KeyHolding" } }
            )
        ).EnsureSuccessStatusCode();
        (
            await vendor.PostAsJsonAsync("/api/vendor/profile/publish", new { published = true })
        ).EnsureSuccessStatusCode();
        var clientA = await fixture.LoginAsync(ApiFixture.UserA);
        await ApiFixture.WaitUntilAsync(
            async () =>
                (await clientA.GetFromJsonAsync<JsonElement>("/api/marketplace/vendors"))
                    .GetProperty("items")
                    .EnumerateArray()
                    .Any(v => v.GetProperty("orgId").GetGuid() == fixture.OrgB.Value),
            "the published vendor to reach the directory"
        );
        var hierarchy = await clientA.GetAsync("/api/hierarchy");
        Guid rootId;
        if (hierarchy.StatusCode == HttpStatusCode.OK)
            rootId = (await hierarchy.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("nodes")
                .EnumerateArray()
                .First(n => n.GetProperty("depth").GetInt32() == 0)
                .GetProperty("id")
                .GetGuid();
        else
        {
            var created = await clientA.PostAsJsonAsync(
                "/api/hierarchy",
                new { name = "Org A", levels = new[] { "Region" } }
            );
            created.EnsureSuccessStatusCode();
            rootId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("rootNodeId")
                .GetGuid();
        }
        var site = await clientA.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Sealed Store",
                timeZone = "Etc/UTC",
            }
        );
        site.EnsureSuccessStatusCode();
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var created2 = await clientA.PostAsJsonAsync(
            "/api/marketplace/requests",
            new
            {
                vendorOrgId = fixture.OrgB.Value,
                category = "KeyHolding",
                urgency = "Scheduled",
                siteId,
                title = "Sealed request",
                startsAt = DateTimeOffset.UtcNow.AddDays(1),
            }
        );
        created2.EnsureSuccessStatusCode();
        var id = (await created2.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        (
            await clientA.PostAsync($"/api/marketplace/requests/{id}/submit", null)
        ).EnsureSuccessStatusCode();

        var third = await fixture.OperatorClient();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await third.GetAsync($"/api/marketplace/requests/{id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await third.GetAsync($"/api/vendor/requests/{id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await third.PostAsync($"/api/vendor/requests/{id}/accept", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await third.PostAsJsonAsync(
                    $"/api/marketplace/requests/{id}/cancel",
                    new { reason = "x" }
                )
            ).StatusCode
        );
        // the vendor cannot act through the buyer's side, nor the buyer through the vendor's
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await vendor.GetAsync($"/api/marketplace/requests/{id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientA.GetAsync($"/api/vendor/requests/{id}")).StatusCode
        );
        (
            await clientA.PostAsJsonAsync(
                $"/api/marketplace/requests/{id}/cancel",
                new { reason = "done" }
            )
        ).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Bulletins_are_tenant_isolated()
    {
        var clientA = await fixture.LoginAsync(ApiFixture.UserA);
        var created = await clientA.PostAsJsonAsync(
            "/api/bulletins",
            new
            {
                kind = "Safety",
                severity = "Low",
                title = "Isolated bulletin",
                body = "Wet floor.",
            }
        );
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var clientB = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.GetAsync($"/api/bulletins/{id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.PostAsync($"/api/bulletins/{id}/withdraw", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.PostAsJsonAsync($"/api/bulletins/{id}/acknowledge", new { })).StatusCode
        );
        var list = await clientB.GetFromJsonAsync<JsonElement>("/api/bulletins");
        Assert.DoesNotContain(
            list.GetProperty("items").EnumerateArray(),
            b => b.GetProperty("id").GetGuid() == id
        );
    }

    [Fact]
    public async Task Network_shares_are_sealed_to_their_members()
    {
        var clientA = await fixture.LoginAsync(ApiFixture.UserA);
        var created = await clientA.PostAsJsonAsync(
            "/api/network/shares",
            new { name = "Sealed share" }
        );
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var bulletin = await clientA.PostAsJsonAsync(
            $"/api/network/shares/{id}/bulletins",
            new
            {
                kind = "Advisory",
                severity = "Low",
                title = "Sealed bulletin",
                body = "Members only.",
            }
        );
        bulletin.EnsureSuccessStatusCode();
        var bulletinId = (await bulletin.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var clientB = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.GetAsync($"/api/network/shares/{id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.PostAsync($"/api/network/shares/{id}/accept", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await clientB.PostAsJsonAsync(
                    $"/api/network/shares/{id}/invite",
                    new { slug = "org-a" }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await clientB.PostAsync($"/api/network/bulletins/{bulletinId}/import", null)
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await clientB.PostAsync($"/api/network/bulletins/{bulletinId}/withdraw", null)
            ).StatusCode
        );
        var list = await clientB.GetFromJsonAsync<JsonElement>("/api/network/bulletins");
        Assert.DoesNotContain(
            list.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("id").GetGuid() == bulletinId
        );
    }

    [Fact]
    public async Task Patrol_routes_are_tenant_isolated()
    {
        var clientA = await fixture.LoginAsync(ApiFixture.UserA);
        var hierarchy = await clientA.GetAsync("/api/hierarchy");
        Guid rootId;
        if (hierarchy.StatusCode == HttpStatusCode.OK)
            rootId = (await hierarchy.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("nodes")
                .EnumerateArray()
                .First(n => n.GetProperty("depth").GetInt32() == 0)
                .GetProperty("id")
                .GetGuid();
        else
        {
            var created = await clientA.PostAsJsonAsync(
                "/api/hierarchy",
                new { name = "Org A", levels = new[] { "Region" } }
            );
            created.EnsureSuccessStatusCode();
            rootId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("rootNodeId")
                .GetGuid();
        }
        var site = await clientA.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Patrol Isolation Store",
                timeZone = "Etc/UTC",
            }
        );
        site.EnsureSuccessStatusCode();
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var route = await clientA.PostAsJsonAsync(
            "/api/patrols/routes",
            new
            {
                siteId,
                name = "Isolated route",
                checkpoints = new object[] { new { code = "A", label = "A" } },
            }
        );
        route.EnsureSuccessStatusCode();
        var routeId = (await route.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var clientB = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await clientB.PutAsJsonAsync(
                    $"/api/patrols/routes/{routeId}",
                    new
                    {
                        name = "x",
                        checkpoints = new object[] { },
                        archived = false,
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await clientB.PostAsJsonAsync(
                    $"/api/patrols/routes/{routeId}/schedules",
                    new
                    {
                        rRule = "FREQ=DAILY",
                        anchorDate = "2026-01-01",
                        startLocal = "09:00",
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clientB.PostAsJsonAsync("/api/patrols", new { routeId })).StatusCode
        );
        var list = await clientB.GetFromJsonAsync<JsonElement>("/api/patrols/routes");
        Assert.DoesNotContain(
            list.GetProperty("items").EnumerateArray(),
            r => r.GetProperty("id").GetGuid() == routeId
        );
    }

    private sealed record SettingDto(Guid Id, string Key, string Value);

    [Fact]
    public async Task Pooled_connection_never_leaks_tenant_context_to_the_next_borrower()
    {
        // No Reset On Close disables the pool's DISCARD ALL - the exact
        // configuration where a session GUC would survive into the next
        // borrower. The interceptor sets the GUC on EVERY open (org or ''),
        // so isolation holds by construction, not by pool behavior (ADR 38).
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(fixture.AppConnectionString)
        {
            NoResetOnClose = true,
            MaxPoolSize = 1, // force reuse of the SAME physical connection
        }.ConnectionString;

        TenancyDbContext Make(TenantContext tenant) =>
            new(
                new DbContextOptionsBuilder<TenancyDbContext>()
                    .UseNpgsql(
                        cs,
                        n =>
                            LocIntel.Platform.Data.ModulePersistence.Configure(
                                n,
                                "tenancy",
                                typeof(TenancyDbContext)
                            )
                    )
                    .AddInterceptors(LocIntel.Platform.Data.TenantSessionInterceptor.Instance)
                    .Options,
                tenant
            );

        // borrower 1: tenanted, warms the pool's one connection with org A
        var tenanted = new TenantContext();
        tenanted.Set(fixture.OrgA, RegionId.Default);
        await using (var db = Make(tenanted))
            Assert.True(
                await db.OrganizationSettings.AnyAsync(),
                "tenanted borrower should see its own org's settings"
            );

        // borrower 2: NO tenant, same physical connection - must see nothing
        await using (var db = Make(new TenantContext()))
        {
            Assert.False(
                await db.OrganizationSettings.IgnoreQueryFilters().AnyAsync(),
                "a tenantless borrower inherited the previous borrower's tenant context"
            );
            var guc = (
                await db
                    .Database.SqlQueryRaw<string>(
                        "select current_setting('app.org_id', true) as \"Value\""
                    )
                    .ToListAsync()
            ).First();
            Assert.Equal("", guc);
        }
    }
}
