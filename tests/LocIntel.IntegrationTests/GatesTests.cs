using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The three gates working together (ADRs 6/8/9/10/11): entitlement failures
/// are 402, grant failures are 403, scope narrows silently.
/// </summary>
public class GatesTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private async Task<(HttpClient owner, Guid rootId)> Setup()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var get = await owner.GetAsync("/api/hierarchy");
        if (get.StatusCode == HttpStatusCode.OK)
        {
            var tree = await get.Content.ReadFromJsonAsync<JsonElement>();
            return (
                owner,
                tree.GetProperty("nodes")
                    .EnumerateArray()
                    .First(n => n.GetProperty("depth").GetInt32() == 0)
                    .GetProperty("id")
                    .GetGuid()
            );
        }
        var created = await owner.PostAsJsonAsync(
            "/api/hierarchy",
            new { name = "Org A", levels = new[] { "Region", "Market" } }
        );
        created.EnsureSuccessStatusCode();
        return (
            owner,
            (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("rootNodeId")
                .GetGuid()
        );
    }

    // ---- gate 1: entitlements ----

    [Fact]
    public async Task Hierarchy_depth_over_plan_is_402()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var response = await owner.PostAsJsonAsync(
            "/api/hierarchy",
            new { name = "Deep", levels = new[] { "A", "B", "C", "D", "E" } }
        ); // default cap: 4
        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task Site_limit_blocks_with_402_and_exception_lifts_it()
    {
        var (owner, rootId) = await Setup();

        var existing = (await ApiFixture.GetItemsAsync(owner, "/api/sites")).GetArrayLength();
        // custody: the OPERATOR sets the tenant's limit
        var op = await fixture.OperatorClient();
        var set = await op.PutAsJsonAsync(
            $"/api/operator/orgs/{fixture.OrgA.Value}/entitlements/sites.max",
            new { value = (existing + 1).ToString() }
        );
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var first = await owner.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Limit 1",
                timeZone = "Etc/UTC",
            }
        );
        first.EnsureSuccessStatusCode();

        var second = await owner.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Limit 2",
                timeZone = "Etc/UTC",
            }
        );
        Assert.Equal(HttpStatusCode.PaymentRequired, second.StatusCode);

        // first-class exception (ADR 10): operator-granted, then it just works
        var exception = await op.PostAsJsonAsync(
            $"/api/operator/orgs/{fixture.OrgA.Value}/entitlements/sites.max/exceptions",
            new
            {
                // generous: this exception outlives the test (an hour) and
                // becomes the org's ceiling for everything else in the class
                value = (existing + 500).ToString(),
                reason = "sales promised",
                expiresAt = DateTimeOffset.UtcNow.AddHours(1),
            }
        );
        exception.EnsureSuccessStatusCode();

        var third = await owner.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Limit 3",
                timeZone = "Etc/UTC",
            }
        );
        third.EnsureSuccessStatusCode();

        // leave the fixture as found: xUnit's in-class order is a hash of the
        // test name (the fork rename reshuffled it), and later tests in this
        // class create sites too
        (
            await op.PutAsJsonAsync(
                $"/api/operator/orgs/{fixture.OrgA.Value}/entitlements/sites.max",
                new { value = "100" }
            )
        ).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Downgrade_below_usage_is_409_with_preflight_detail()
    {
        var (owner, rootId) = await Setup(); // hierarchy uses 2 levels
        var op = await fixture.OperatorClient();
        var response = await op.PutAsJsonAsync(
            $"/api/operator/orgs/{fixture.OrgA.Value}/entitlements/hierarchy.depth",
            new { value = "1" }
        );
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("over").GetInt64());
    }

    [Fact]
    public async Task Metered_grace_allows_ten_percent_then_blocks()
    {
        // Explicit about WHICH org is metered: the "cold-boot metering loss"
        // this test used to blame was never metering at all - a multi-org
        // user's default org was a per-boot coin flip (CreatedAt tie at
        // Postgres microsecond resolution), so some boots metered org B
        // against org A's operator-set limit.
        var owner = await fixture.LoginAsync(ApiFixture.UserBoth);
        (
            await owner.PostAsJsonAsync("/auth/switch-org", new { orgId = fixture.OrgA.Value })
        ).EnsureSuccessStatusCode();

        var op = await fixture.OperatorClient();
        var set = await op.PutAsJsonAsync(
            $"/api/operator/orgs/{fixture.OrgA.Value}/entitlements/contact_links.monthly",
            new { value = "10" }
        );
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        var effective = await op.GetFromJsonAsync<JsonElement>(
            $"/api/operator/orgs/{fixture.OrgA.Value}/entitlements"
        );
        Assert.Equal(
            "10",
            effective.GetProperty("contact_links.monthly").GetProperty("value").GetString()
        );

        var results = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++)
        {
            var response = await owner.PostAsJsonAsync(
                "/contact-links",
                new { email = $"m{i}@example.com" }
            );
            results.Add(response.StatusCode);
        }
        // 10 within limit + 1 grace (10 * 1.1), the 12th blocked
        Assert.Equal(11, results.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(HttpStatusCode.PaymentRequired, results[^1]);
    }

    [Fact]
    public async Task Boolean_off_gates_the_feature_with_402()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserB); // org B: separate tenant
        var op = await fixture.OperatorClient();
        var set = await op.PutAsJsonAsync(
            $"/api/operator/orgs/{fixture.OrgB.Value}/entitlements/contact_links.enabled",
            new { value = "false" }
        );
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var response = await owner.PostAsJsonAsync(
            "/contact-links",
            new { email = "x@example.com" }
        );
        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task Effective_entitlements_are_readable_with_usage()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var entitlements = await owner.GetFromJsonAsync<JsonElement>("/api/entitlements");
        Assert.Equal(
            "90",
            entitlements.GetProperty("audit.retention_days").GetProperty("value").GetString()
        );
        Assert.Equal(
            "Tiered",
            entitlements.GetProperty("audit.retention_days").GetProperty("shape").GetString()
        );

        // usage rides along where the system can know it (UX: "used X of Y")
        var siteCount = (await ApiFixture.GetItemsAsync(owner, "/api/sites")).GetArrayLength();
        Assert.Equal(
            siteCount,
            entitlements.GetProperty("sites.max").GetProperty("usage").GetInt64()
        );
        Assert.True(
            entitlements.GetProperty("contact_links.monthly").GetProperty("usage").GetInt64() >= 0
        );
        // shapes with no meaningful counter say so honestly
        Assert.Equal(
            JsonValueKind.Null,
            entitlements.GetProperty("contact_links.enabled").GetProperty("usage").ValueKind
        );
        Assert.Equal(
            JsonValueKind.Null,
            entitlements.GetProperty("api.requests_per_minute").GetProperty("usage").ValueKind
        );
    }

    [Fact]
    public async Task Org_owner_cannot_set_their_own_entitlements()
    {
        // THE custody rule: a tenant Owner's *:* never crosses the platform wall
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var direct = await owner.PutAsJsonAsync(
            $"/api/operator/orgs/{fixture.OrgA.Value}/entitlements/sites.max",
            new { value = "100000" }
        );
        Assert.Equal(HttpStatusCode.Unauthorized, direct.StatusCode);
        // and the old self-serve surface is gone
        var legacy = await owner.PutAsJsonAsync(
            "/api/admin/entitlements/sites.max",
            new { value = "100000" }
        );
        Assert.Equal(HttpStatusCode.NotFound, legacy.StatusCode);
    }

    [Fact]
    public async Task Suspension_blocks_the_org_and_reactivation_restores_it()
    {
        var op = await fixture.OperatorClient();
        var member = await fixture.LoginAsync(ApiFixture.UserB);
        (await member.GetAsync("/api/sites")).EnsureSuccessStatusCode();

        (
            await op.PostAsync($"/api/operator/orgs/{fixture.OrgB.Value}/suspend", null)
        ).EnsureSuccessStatusCode();
        HttpResponseMessage blocked = null!;
        for (var i = 0; i < 50; i++) // enforcement learns via the event
        {
            blocked = await member.GetAsync("/api/sites");
            if (blocked.StatusCode == HttpStatusCode.Forbidden)
                break;
            await Task.Delay(100);
        }
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        (await member.GetAsync("/me")).EnsureSuccessStatusCode(); // /me still works

        (
            await op.PostAsync($"/api/operator/orgs/{fixture.OrgB.Value}/reactivate", null)
        ).EnsureSuccessStatusCode();
        for (var i = 0; i < 50; i++)
        {
            if ((await member.GetAsync("/api/sites")).IsSuccessStatusCode)
                return;
            await Task.Delay(100);
        }
        Assert.Fail("org never reactivated");
    }

    // ---- gates 2+3: grants and scope ----

    [Fact]
    public async Task Member_without_roles_sees_nothing_and_writes_nothing()
    {
        var (owner, rootId) = await Setup();
        var site = await owner.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Visible Store",
                timeZone = "Etc/UTC",
            }
        );
        site.EnsureSuccessStatusCode();

        await fixture.CreateMemberAsync("norole@locintel.local", fixture.OrgA);
        var viewer = await fixture.LoginAsync("norole@locintel.local");
        var list = await ApiFixture.GetItemsAsync(viewer, "/api/sites");
        Assert.Empty(list.EnumerateArray()); // scope None: filters, never errors

        var write = await viewer.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Nope",
                timeZone = "Etc/UTC",
            }
        );
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Subtree_scoped_role_narrows_reads_and_writes()
    {
        var (owner, rootId) = await Setup();
        var east = await Node(owner, rootId, "Scoped East");
        var west = await Node(owner, rootId, "Scoped West");
        await Site(owner, east.id, "East Store");
        await Site(owner, west.id, "West Store");

        // role: sites read+manage, assigned to viewer AT the east subtree only
        var role = await owner.PostAsJsonAsync(
            "/api/roles",
            new { name = "East Manager", grants = new[] { new { domain = "sites", action = "*" } } }
        );
        role.EnsureSuccessStatusCode();
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var viewerId = await fixture.CreateMemberAsync("eastmgr@locintel.local", fixture.OrgA);
        var assign = await owner.PostAsJsonAsync(
            $"/api/roles/{roleId}/assign",
            new { userId = viewerId, scopePath = east.path }
        );
        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);

        var viewer = await fixture.LoginAsync("eastmgr@locintel.local");
        var visible = await ApiFixture.GetItemsAsync(viewer, "/api/sites");
        var names = visible
            .EnumerateArray()
            .Select(s => s.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("East Store", names);
        Assert.DoesNotContain("West Store", names); // scope filters silently

        // id-addressed read outside the subtree scope: 404 (never confirm)
        var westSites = await ApiFixture.GetItemsAsync(owner, $"/api/sites?under={west.id}");
        var westSiteId = westSites.EnumerateArray().First().GetProperty("id").GetGuid();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await viewer.GetAsync($"/api/sites/{westSiteId}")).StatusCode
        );

        // write inside the subtree: allowed; outside: 403
        var inside = await viewer.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = east.id,
                name = "East Annex",
                timeZone = "Etc/UTC",
            }
        );
        inside.EnsureSuccessStatusCode();
        var outside = await viewer.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = west.id,
                name = "West Annex",
                timeZone = "Etc/UTC",
            }
        );
        Assert.Equal(HttpStatusCode.Forbidden, outside.StatusCode);
    }

    [Fact]
    public async Task Grant_exception_is_additive_and_time_boxed()
    {
        var (owner, rootId) = await Setup();
        var viewerId = await fixture.CreateMemberAsync("temp-cover@locintel.local", fixture.OrgA);

        var exception = await owner.PostAsJsonAsync(
            "/api/grant-exceptions",
            new
            {
                userId = viewerId,
                domain = "hierarchy",
                action = "manage",
                reason = "covering reorg while manager is out",
                expiresAt = DateTimeOffset.UtcNow.AddHours(1),
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, exception.StatusCode);

        var viewer = await fixture.LoginAsync("temp-cover@locintel.local");
        var node = await viewer.PostAsJsonAsync(
            "/api/hierarchy/nodes",
            new { parentId = rootId, name = "Exception Region" }
        );
        node.EnsureSuccessStatusCode(); // the exception grants exactly this

        var site = await viewer.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Still Nope",
                timeZone = "Etc/UTC",
            }
        );
        Assert.Equal(HttpStatusCode.Forbidden, site.StatusCode); // additive, not broad
    }

    private static async Task<(Guid id, string path)> Node(
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

    private static async Task Site(HttpClient client, Guid nodeId, string name)
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
    }
}
