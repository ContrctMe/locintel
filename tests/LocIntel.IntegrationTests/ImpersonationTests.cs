using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Support impersonation (ADR 42): a time-boxed, claims-only session into a
/// tenant org - Owner-equivalent inside it, platform reach stripped, both
/// ends audited into the target org.
/// </summary>
public class ImpersonationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Operator_sees_the_target_org_and_loses_the_platform_wall()
    {
        var op = await fixture.OperatorClient();
        var started = await op.PostAsync(
            $"/api/operator/orgs/{fixture.OrgA.Value}/impersonate",
            null
        );
        Assert.True(started.IsSuccessStatusCode, await started.Content.ReadAsStringAsync());

        // the session IS the target org now: RLS + scope resolve to OrgA
        var me = await op.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal(fixture.OrgA.Value, me.GetProperty("activeOrg").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, me.GetProperty("impersonationExpiresAt").ValueKind);
        Assert.Contains(
            me.GetProperty("organizations").EnumerateArray(),
            o => o.GetProperty("id").GetGuid() == fixture.OrgA.Value
        );
        // Owner-equivalent means WRITES too: bootstrap a hierarchy and a
        // site as support would when fixing a tenant's setup
        var bootstrap = await op.PostAsJsonAsync(
            "/api/hierarchy",
            new { name = "Org A", levels = new[] { "Region" } }
        );
        Assert.True(bootstrap.IsSuccessStatusCode, await bootstrap.Content.ReadAsStringAsync());
        var rootId = (await bootstrap.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("rootNodeId")
            .GetGuid();
        (
            await op.PostAsJsonAsync(
                "/api/sites",
                new
                {
                    nodeId = rootId,
                    name = "Support Fixed This",
                    timeZone = "Etc/UTC",
                }
            )
        ).EnsureSuccessStatusCode();
        var sites = await ApiFixture.GetItemsAsync(op, "/api/sites");
        Assert.True(sites.GetArrayLength() > 0);

        // the operator wall drops WHILE impersonating: no platform reach,
        // no chaining into a second org
        var wall = await op.GetAsync($"/api/operator/orgs/{fixture.OrgB.Value}/entitlements");
        Assert.Equal(HttpStatusCode.Forbidden, wall.StatusCode);
        var chain = await op.PostAsync(
            $"/api/operator/orgs/{fixture.OrgB.Value}/impersonate",
            null
        );
        Assert.Equal(HttpStatusCode.Forbidden, chain.StatusCode);

        // stop: home to the platform org, audit trail on record in the target
        (await op.PostAsync("/auth/impersonation/stop", null)).EnsureSuccessStatusCode();
        var after = await op.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal(JsonValueKind.Null, after.GetProperty("impersonationExpiresAt").ValueKind);
        Assert.Equal(fixture.PlatformOrg.Value, after.GetProperty("activeOrg").GetGuid());

        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var seen = (started: false, ended: false);
        for (var i = 0; i < 200 && seen is not (true, true); i++)
        {
            var events = await owner.GetFromJsonAsync<JsonElement>("/api/audit/events");
            foreach (var row in events.EnumerateArray())
            {
                var action = row.GetProperty("eventName").GetString();
                if (action == "operator.impersonation.started")
                    seen.started = true;
                if (action == "operator.impersonation.ended")
                    seen.ended = true;
            }
            if (seen is not (true, true))
                await Task.Delay(100);
        }
        Assert.True(seen.started && seen.ended, await fixture.DeadLetterSummary());
    }

    [Fact]
    public async Task Only_operators_impersonate_and_never_the_platform_org()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var denied = await owner.PostAsync(
            $"/api/operator/orgs/{fixture.OrgB.Value}/impersonate",
            null
        );
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var op = await fixture.OperatorClient();
        var platform = await op.PostAsync(
            $"/api/operator/orgs/{fixture.PlatformOrg.Value}/impersonate",
            null
        );
        Assert.Equal(HttpStatusCode.BadRequest, platform.StatusCode);
    }
}

/// <summary>
/// Three-second TTL: the expiry claim is the whole enforcement story. (Not
/// one second - test-host warmup can eat that before the liveness probe.)
/// </summary>
public sealed class ShortImpersonationFixture : ApiFixture
{
    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        base.ConfigureHost(builder);
        builder.UseSetting("Impersonation:TtlSeconds", "3");
    }
}

public class ImpersonationExpiryTests(ShortImpersonationFixture fixture)
    : IClassFixture<ShortImpersonationFixture>
{
    [Fact]
    public async Task Expired_sessions_resolve_to_nothing()
    {
        var op = await fixture.OperatorClient();
        (
            await op.PostAsync($"/api/operator/orgs/{fixture.OrgA.Value}/impersonate", null)
        ).EnsureSuccessStatusCode();

        // live now (a grant-guarded endpoint answers); after the claim lapses
        // the operator is still signed in with OrgA active but holds no grant
        // there, so the grant gate answers 403 - no cleanup job involved
        var live = await op.GetAsync("/api/members");
        Assert.True(
            live.IsSuccessStatusCode,
            $"{(int)live.StatusCode}: {await live.Content.ReadAsStringAsync()}"
        );
        var expired = false;
        for (var i = 0; i < 100 && !expired; i++)
        {
            expired = (await op.GetAsync("/api/members")).StatusCode == HttpStatusCode.Forbidden;
            if (!expired)
                await Task.Delay(100);
        }
        Assert.True(expired);

        // and the expired cookie still finds its way home
        (await op.PostAsync("/auth/impersonation/stop", null)).EnsureSuccessStatusCode();
        var me = await op.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal(fixture.PlatformOrg.Value, me.GetProperty("activeOrg").GetGuid());
    }
}
