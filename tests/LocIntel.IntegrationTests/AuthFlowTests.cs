using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

public class AuthFlowTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Multi_org_default_org_is_first_joined_deterministically()
    {
        // UserBoth's two memberships were seeded microseconds apart - their
        // CreatedAt values collide at Postgres resolution. The UUIDv7 id
        // tie-break makes the pick deterministic: first joined wins. (Without
        // it this was a per-boot coin flip that masqueraded as cold-boot
        // metering loss.)
        var client = await fixture.LoginAsync(ApiFixture.UserBoth);
        var me = await client.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal(fixture.OrgA.Value, me.GetProperty("activeOrg").GetGuid());
    }

    [Fact]
    public async Task Login_yields_user_principal_with_memberships()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserA);
        var me = await client.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal("user", me.GetProperty("tier").GetString());
        Assert.Equal(ApiFixture.UserA, me.GetProperty("email").GetString());
        Assert.Equal(fixture.OrgA.Value, me.GetProperty("activeOrg").GetGuid());
        Assert.Single(me.GetProperty("organizations").EnumerateArray());
    }

    [Fact]
    public async Task Unauthenticated_is_guest_not_error()
    {
        var me = await fixture.GuestClient().GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal("guest", me.GetProperty("tier").GetString());
    }

    [Fact]
    public async Task Switch_org_changes_active_tenant()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserBoth);

        var switchTo = await client.PostAsJsonAsync(
            "/auth/switch-org",
            new { orgId = fixture.OrgB.Value }
        );
        Assert.Equal(HttpStatusCode.NoContent, switchTo.StatusCode);

        var settings = await client.GetFromJsonAsync<JsonElement>("/api/settings");
        var colors = settings
            .EnumerateArray()
            .Where(s => s.GetProperty("key").GetString() == "brand.color")
            .Select(s => s.GetProperty("value").GetString())
            .ToList();
        Assert.Equal(["#0A6E8A"], colors); // org B's world now

        var me = await client.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal(fixture.OrgB.Value, me.GetProperty("activeOrg").GetGuid());
    }

    [Fact]
    public async Task Switch_to_non_member_org_is_404()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserA);
        var response = await client.PostAsJsonAsync(
            "/auth/switch-org",
            new { orgId = fixture.OrgB.Value }
        );
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OAuth_error_callback_redirects_to_login_with_reason()
    {
        // provider error branch (cancelled login, unknown user): no code, no crash
        var client = fixture.Factory.CreateDefaultClient(
            new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler()
        );
        var response = await client.GetAsync("/auth/callback?error=user_not_found&state=whatever");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/?authError=user_not_found", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Logout_returns_to_guest()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserA);
        (await client.PostAsync("/auth/logout", null)).EnsureSuccessStatusCode();
        var me = await client.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal("guest", me.GetProperty("tier").GetString());
    }
}
