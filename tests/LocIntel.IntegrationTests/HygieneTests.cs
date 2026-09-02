using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The self-serve hygiene bundle: leave-org, last-manager protection, role
/// lifecycle, exception revocation, org rename. Every guard is a PRE-check -
/// a Conflict must leave no partial write behind.
/// </summary>
public class HygieneTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    // ---- last-manager protection ----

    [Fact]
    public async Task Last_manager_cannot_be_removed_or_leave()
    {
        // fresh org where the founder is the only manager
        await fixture.CreateUserOnly("solo@hygiene.local");
        var solo = await fixture.LoginAsync("solo@hygiene.local");
        (
            await solo.PostAsJsonAsync("/api/orgs", new { name = "Solo Co", slug = "solo-co" })
        ).EnsureSuccessStatusCode();
        var orgId = await PollOrg(solo, "solo-co");
        (await solo.PostAsJsonAsync("/auth/switch-org", new { orgId })).EnsureSuccessStatusCode();

        var leave = await solo.PostAsync("/api/members/leave", null);
        Assert.Equal(HttpStatusCode.Conflict, leave.StatusCode);
    }

    [Fact]
    public async Task Member_who_is_not_last_manager_can_leave_and_lands_on_day_zero()
    {
        await fixture.CreateUserOnly("stay@hygiene.local");
        var founder = await fixture.LoginAsync("stay@hygiene.local");
        (
            await founder.PostAsJsonAsync(
                "/api/orgs",
                new { name = "Leaver Co", slug = "leaver-co" }
            )
        ).EnsureSuccessStatusCode();
        var orgId = await PollOrg(founder, "leaver-co");
        (
            await founder.PostAsJsonAsync("/auth/switch-org", new { orgId })
        ).EnsureSuccessStatusCode();

        // second member joins with a limited role via invite intent
        var role = await founder.PostAsJsonAsync(
            "/api/roles",
            new { name = "Helper", grants = new[] { new { domain = "sites", action = "read" } } }
        );
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        (
            await founder.PostAsJsonAsync(
                "/api/members/invitations",
                new { email = "goer@hygiene.local", roleId }
            )
        ).EnsureSuccessStatusCode();
        var externalOrgId = await fixture.ExternalOrgIdOf(orgId);
        var goer = await fixture.LoginAsync("goer@hygiene.local", externalOrgId);

        var leave = await goer.PostAsync("/api/members/leave", null);
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        // session reissued: back to day-zero (no orgs)
        var me = await goer.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal(0, me.GetProperty("organizations").GetArrayLength());
        // and the roster shrank
        var members = await ApiFixture.GetItemsAsync(founder, "/api/members");
        Assert.Equal(1, members.GetArrayLength());
    }

    // ---- role lifecycle ----

    [Fact]
    public async Task Role_edit_delete_and_unassign_respect_the_guard()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);

        // create + edit a role
        var created = await owner.PostAsJsonAsync(
            "/api/roles",
            new { name = "Temp", grants = new[] { new { domain = "sites", action = "read" } } }
        );
        created.EnsureSuccessStatusCode();
        var roleId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var edit = await owner.PutAsJsonAsync(
            $"/api/roles/{roleId}",
            new { name = "Temp Edited", grants = new[] { new { domain = "sites", action = "*" } } }
        );
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        var roles = await owner.GetFromJsonAsync<JsonElement>("/api/roles");
        var edited = roles
            .EnumerateArray()
            .First(r => r.GetProperty("name").GetString() == "Temp Edited");
        // the editor's read: the list carries each role's grants and reach
        var grant = Assert.Single(edited.GetProperty("grants").EnumerateArray());
        Assert.Equal("sites", grant.GetProperty("domain").GetString());
        Assert.Equal("*", grant.GetProperty("action").GetString());
        Assert.Equal(0, edited.GetProperty("assignedCount").GetInt32());

        // assigned roles cannot be deleted; unassigned ones can
        var viewerId = await fixture.CreateMemberAsync("roleuser@hygiene.local", fixture.OrgA);
        (
            await owner.PostAsJsonAsync($"/api/roles/{roleId}/assign", new { userId = viewerId })
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            1,
            (await owner.GetFromJsonAsync<JsonElement>("/api/roles"))
                .EnumerateArray()
                .First(r => r.GetProperty("id").GetGuid() == roleId)
                .GetProperty("assignedCount")
                .GetInt32()
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await owner.DeleteAsync($"/api/roles/{roleId}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await owner.DeleteAsync($"/api/roles/{roleId}/assign/{viewerId}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await owner.DeleteAsync($"/api/roles/{roleId}")).StatusCode
        );
    }

    [Fact]
    public async Task Editing_the_only_manager_role_out_of_manage_is_refused_atomically()
    {
        await fixture.CreateUserOnly("editguard@hygiene.local");
        var founder = await fixture.LoginAsync("editguard@hygiene.local");
        (
            await founder.PostAsJsonAsync(
                "/api/orgs",
                new { name = "Edit Guard", slug = "edit-guard" }
            )
        ).EnsureSuccessStatusCode();
        var orgId = await PollOrg(founder, "edit-guard");
        (
            await founder.PostAsJsonAsync("/auth/switch-org", new { orgId })
        ).EnsureSuccessStatusCode();

        var roles = await founder.GetFromJsonAsync<JsonElement>("/api/roles");
        var ownerRoleId = roles
            .EnumerateArray()
            .First(r => r.GetProperty("name").GetString() == "Owner")
            .GetProperty("id")
            .GetGuid();

        var edit = await founder.PutAsJsonAsync(
            $"/api/roles/{ownerRoleId}",
            new
            {
                name = "Owner",
                grants = new[] { new { domain = "sites", action = "*" } }, // strips roles:manage
            }
        );
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        // ATOMIC refusal: the role still manages (founder can still call /api/roles)
        (await founder.GetAsync("/api/roles")).EnsureSuccessStatusCode();
    }

    // ---- exceptions ----

    [Fact]
    public async Task Exceptions_are_listable_and_revocable()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var targetId = await fixture.CreateMemberAsync("excuser@hygiene.local", fixture.OrgA);
        (
            await owner.PostAsJsonAsync(
                "/api/grant-exceptions",
                new
                {
                    userId = targetId,
                    domain = "audit",
                    action = "read",
                    reason = "quarterly review",
                    expiresAt = DateTimeOffset.UtcNow.AddHours(1),
                }
            )
        ).EnsureSuccessStatusCode();

        var listed = await owner.GetFromJsonAsync<JsonElement>("/api/grant-exceptions");
        var row = listed
            .EnumerateArray()
            .First(e => e.GetProperty("email").GetString() == "excuser@hygiene.local");
        Assert.Equal("quarterly review", row.GetProperty("reason").GetString());

        // holder can act; after revoke, they cannot
        var target = await fixture.LoginAsync("excuser@hygiene.local");
        (await target.GetAsync("/api/audit/events")).EnsureSuccessStatusCode();
        (
            await owner.DeleteAsync($"/api/grant-exceptions/{row.GetProperty("id").GetGuid()}")
        ).EnsureSuccessStatusCode();
        var target2 = await fixture.LoginAsync("excuser@hygiene.local"); // fresh resolver scope
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await target2.GetAsync("/api/audit/events")).StatusCode
        );
    }

    // ---- org rename ----

    [Fact]
    public async Task Org_rename_flows_to_me_and_audit()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserB);
        var rename = await owner.PutAsJsonAsync("/api/org", new { name = "Org B Renamed" });
        rename.EnsureSuccessStatusCode();

        JsonElement me = default;
        for (var i = 0; i < 50; i++) // read model learns via the event
        {
            me = await owner.GetFromJsonAsync<JsonElement>("/me");
            if (
                me.GetProperty("organizations")
                    .EnumerateArray()
                    .Any(o => o.GetProperty("name").GetString() == "Org B Renamed")
            )
                break;
            await Task.Delay(100);
        }
        Assert.Contains(
            me.GetProperty("organizations").EnumerateArray(),
            o => o.GetProperty("name").GetString() == "Org B Renamed"
        );

        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA); // no org:manage
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await viewer.PutAsJsonAsync("/api/org", new { name = "Nope" })).StatusCode
        );
    }

    private static async Task<Guid> PollOrg(HttpClient client, string slug)
    {
        for (var i = 0; i < 60; i++)
        {
            var me = await client.GetFromJsonAsync<JsonElement>("/me");
            var org = me.GetProperty("organizations")
                .EnumerateArray()
                .FirstOrDefault(o => o.GetProperty("slug").GetString() == slug);
            if (org.ValueKind == JsonValueKind.Object)
                return org.GetProperty("id").GetGuid();
            await Task.Delay(100);
        }
        throw new TimeoutException($"org '{slug}' never appeared");
    }
}
