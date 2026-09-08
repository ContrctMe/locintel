using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Modules.Identity.Access;

namespace LocIntel.IntegrationTests;

/// <summary>
/// What an org is born with (flow review, 2026-09): a hierarchy whose root
/// carries the org's name, and the preset roles - so the first site and the
/// first invite need no provisioning step and no capability codes.
/// </summary>
public class OrgDefaultsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private async Task<(HttpClient founder, Guid orgId)> NewOrg(string email, string slug)
    {
        await fixture.CreateUserOnly(email);
        var founder = await fixture.LoginAsync(email);
        var created = await founder.PostAsJsonAsync(
            "/api/orgs",
            new { name = "Newco Coffee", slug }
        );
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var orgId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("orgId")
            .GetGuid();
        await ApiFixture.WaitForMembershipAsync(founder);
        (
            await founder.PostAsJsonAsync("/auth/switch-org", new { orgId })
        ).EnsureSuccessStatusCode();
        return (founder, orgId);
    }

    [Fact]
    public async Task A_new_org_has_a_root_named_after_it_and_the_default_levels()
    {
        var (founder, _) = await NewOrg("defaults-tree@newco.local", "defaults-tree");
        var rootId = await ApiFixture.WaitForRootAsync(founder);

        var tree = await founder.GetFromJsonAsync<JsonElement>("/api/hierarchy");
        Assert.Equal("Newco Coffee", tree.GetProperty("name").GetString());
        Assert.Equal(
            ["Region", "Market"],
            tree.GetProperty("levels").EnumerateArray().Select(l => l.GetString()!).ToArray()
        );
        var root = tree.GetProperty("nodes").EnumerateArray().Single();
        Assert.Equal(rootId, root.GetProperty("id").GetGuid());
        Assert.Equal("Newco Coffee", root.GetProperty("name").GetString());

        // the first site sits on the root with no other step
        var site = await founder.PostAsJsonAsync(
            "/api/sites",
            new
            {
                name = "Pike Place",
                nodeId = rootId,
                timeZone = "America/Los_Angeles",
            }
        );
        Assert.True(site.IsSuccessStatusCode, await site.Content.ReadAsStringAsync());

        // provisioning by hand is now a conflict, never a second tree
        var again = await founder.PostAsJsonAsync(
            "/api/hierarchy",
            new { name = "Again", levels = new[] { "A" } }
        );
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Levels_can_be_renamed_grown_and_not_shrunk_under_a_node()
    {
        var (founder, _) = await NewOrg("defaults-levels@newco.local", "defaults-levels");
        var rootId = await ApiFixture.WaitForRootAsync(founder);

        var renamed = await founder.PutAsJsonAsync(
            "/api/hierarchy",
            new { levels = new[] { "Division", "District", "Store group" } }
        );
        Assert.True(renamed.IsSuccessStatusCode, await renamed.Content.ReadAsStringAsync());
        var tree = await founder.GetFromJsonAsync<JsonElement>("/api/hierarchy");
        Assert.Equal(
            ["Division", "District", "Store group"],
            tree.GetProperty("levels").EnumerateArray().Select(l => l.GetString()!).ToArray()
        );

        // a node at depth 1 pins the count at one or more
        (
            await founder.PostAsJsonAsync(
                "/api/hierarchy/nodes",
                new { parentId = rootId, name = "West" }
            )
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await founder.PutAsJsonAsync(
                    "/api/hierarchy",
                    new { levels = Array.Empty<string>() }
                )
            ).StatusCode
        );
        var one = await founder.PutAsJsonAsync("/api/hierarchy", new { levels = new[] { "Area" } });
        Assert.True(one.IsSuccessStatusCode, await one.Content.ReadAsStringAsync());

        // past the plan's depth is gate 1, not an error
        Assert.Equal(
            HttpStatusCode.PaymentRequired,
            (
                await founder.PutAsJsonAsync(
                    "/api/hierarchy",
                    new { levels = new[] { "A", "B", "C", "D", "E" } }
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task A_new_org_has_the_preset_roles_and_the_founder_is_owner()
    {
        var (founder, _) = await NewOrg("defaults-roles@newco.local", "defaults-roles");

        var roles = await founder.GetFromJsonAsync<JsonElement>("/api/roles");
        var byName = roles
            .EnumerateArray()
            .ToDictionary(r => r.GetProperty("name").GetString()!, r => r);
        Assert.Equal(
            RolePresets.All.Select(p => p.Name).OrderBy(n => n),
            byName.Keys.OrderBy(n => n)
        );
        Assert.Equal(1, byName["Owner"].GetProperty("assignedCount").GetInt32());
        Assert.Equal(0, byName["Site manager"].GetProperty("assignedCount").GetInt32());
        var siteManager = byName["Site manager"]
            .GetProperty("grants")
            .EnumerateArray()
            .Select(g =>
                $"{g.GetProperty("domain").GetString()}:{g.GetProperty("action").GetString()}"
            )
            .ToHashSet();
        Assert.Contains("checklists:complete", siteManager);
        Assert.DoesNotContain("sites:manage", siteManager);
    }
}
