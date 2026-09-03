using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The day-zero arc: a fresh user creates an org (founder bootstrap), invites
/// a colleague with a role intent, the colleague accepts and lands with
/// exactly that role, and members can be managed.
/// </summary>
public class DayZeroTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Fresh_user_creates_org_and_becomes_owner()
    {
        await fixture.CreateUserOnly("founder@newco.local");
        var founder = await fixture.LoginAsync("founder@newco.local");

        // no orgs yet
        var before = await founder.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal(0, before.GetProperty("organizations").GetArrayLength());

        var created = await founder.PostAsJsonAsync(
            "/api/orgs",
            new { name = "NewCo", slug = "newco" }
        );
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var orgId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("orgId")
            .GetGuid();

        // founder membership + Owner arrive via the outbox; then switch in
        JsonElement me = default;
        me = await ApiFixture.WaitForMembershipAsync(founder);
        Assert.Equal("newco", me.GetProperty("organizations")[0].GetProperty("slug").GetString());

        (
            await founder.PostAsJsonAsync("/auth/switch-org", new { orgId })
        ).EnsureSuccessStatusCode();
        var active = await founder.GetFromJsonAsync<JsonElement>("/me");
        // founder is Owner (*:*): every capability EXCEPT platform reach -
        // the org flag is the operator wall, never advertised to tenants
        var capabilities = active
            .GetProperty("capabilities")
            .EnumerateArray()
            .Select(c => c.GetString())
            .ToHashSet();
        Assert.Equal(LocIntel.Platform.Kernel.Capabilities.All.Count - 1, capabilities.Count);
        Assert.DoesNotContain(LocIntel.Platform.Kernel.Capabilities.PlatformOperate, capabilities);
    }

    [Fact]
    public async Task Duplicate_slug_is_conflict()
    {
        await fixture.CreateUserOnly("founder2@dupe.local");
        var founder = await fixture.LoginAsync("founder2@dupe.local");
        (
            await founder.PostAsJsonAsync("/api/orgs", new { name = "Dupe", slug = "dupe-co" })
        ).EnsureSuccessStatusCode();
        var second = await founder.PostAsJsonAsync(
            "/api/orgs",
            new { name = "Dupe2", slug = "dupe-co" }
        );
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Invite_carries_role_intent_and_acceptance_applies_it()
    {
        // founder with an org (local provider: in-memory directory)
        await fixture.CreateUserOnly("boss@inviteco.local");
        var boss = await fixture.LoginAsync("boss@inviteco.local");
        (
            await boss.PostAsJsonAsync("/api/orgs", new { name = "InviteCo", slug = "inviteco" })
        ).EnsureSuccessStatusCode();
        JsonElement me = default;
        me = await ApiFixture.WaitForMembershipAsync(boss);
        var orgId = me.GetProperty("organizations")[0].GetProperty("id").GetGuid();
        (await boss.PostAsJsonAsync("/auth/switch-org", new { orgId })).EnsureSuccessStatusCode();

        // a limited role for the invitee
        var role = await boss.PostAsJsonAsync(
            "/api/roles",
            new { name = "Analyst", grants = new[] { new { domain = "sites", action = "read" } } }
        );
        role.EnsureSuccessStatusCode();
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        // invite: provider delivers; we record intent
        var invite = await boss.PostAsJsonAsync(
            "/api/members/invitations",
            new { email = "analyst@inviteco.local", roleId }
        );
        Assert.True(invite.IsSuccessStatusCode, await invite.Content.ReadAsStringAsync());

        var pending = await boss.GetFromJsonAsync<JsonElement>("/api/members/invitations");
        var row = Assert.Single(pending.EnumerateArray());
        Assert.Equal("Analyst", row.GetProperty("role").GetString());

        // acceptance == first login through the org (the provider reports the
        // external org id); simulate what AuthKit does after accept
        var externalOrgId = await fixture.ExternalOrgIdOf(orgId);
        var analyst = await fixture.LoginAsync("analyst@inviteco.local", externalOrgId);
        var analystMe = await analyst.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal(orgId, analystMe.GetProperty("activeOrg").GetGuid());
        var capabilities = analystMe
            .GetProperty("capabilities")
            .EnumerateArray()
            .Select(c => c.GetString())
            .ToList();
        Assert.Equal(["sites:read"], capabilities); // exactly the intent, nothing more

        // members list shows both with roles
        var members = await ApiFixture.GetItemsAsync(boss, "/api/members");
        Assert.Equal(2, members.GetArrayLength());
        var analystRow = members
            .EnumerateArray()
            .First(m => m.GetProperty("email").GetString() == "analyst@inviteco.local");
        Assert.Equal("Analyst", analystRow.GetProperty("roles")[0].GetString());

        // removal: analyst loses access, list shrinks
        var analystId = analystRow.GetProperty("userId").GetGuid();
        (await boss.DeleteAsync($"/api/members/{analystId}")).EnsureSuccessStatusCode();
        var after = await ApiFixture.GetItemsAsync(boss, "/api/members");
        Assert.Equal(1, after.GetArrayLength());

        // self-removal is refused
        var bossId = after[0].GetProperty("userId").GetGuid();
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await boss.DeleteAsync($"/api/members/{bossId}")).StatusCode
        );
    }

    [Fact]
    public async Task Member_without_roles_manage_cannot_see_members()
    {
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/members")).StatusCode);
    }
}
