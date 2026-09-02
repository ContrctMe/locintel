using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The lifecycle tail (ADR 25): export is self-serve data portability (the
/// archive lands in the org's own file library), offboarding is operator
/// custody gated on a prior suspension. Data purges module-by-module; the org
/// anchor row and the audit trail remain.
/// </summary>
public class LifecycleTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Self_serve_export_lands_in_files_with_every_module_section()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);

        var queued = await owner.PostAsync("/api/org/export", null);
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);

        // the archive arrives async through the outbox, as a regular Clean file
        JsonElement export = default;
        var found = false;
        for (var i = 0; i < 100 && !found; i++)
        {
            var files = await ApiFixture.GetItemsAsync(owner, "/api/files");
            foreach (var file in files.EnumerateArray())
            {
                if (
                    file.GetProperty("name").GetString()!.StartsWith("org-export-")
                    && file.GetProperty("status").GetString() == "Clean"
                )
                {
                    export = file;
                    found = true;
                }
            }
            if (!found)
                await Task.Delay(100);
        }
        Assert.True(found, "export never landed in the file library");
        Assert.Equal("application/zip", export.GetProperty("contentType").GetString());

        // the EXISTING download flow serves it - same authz, same presigned URL
        var download = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/files/{export.GetProperty("id").GetGuid()}/download"
        );
        var bytes = await owner.GetByteArrayAsync(download.GetProperty("url").GetString());
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        // every module contributed its slice
        var sections = zip.Entries.Select(e => e.Name).Order().ToArray();
        Assert.Equal(
            [
                "alerts.json",
                "audit.json",
                "cases.json",
                "checklists.json",
                "entities.json",
                "entitlements.json",
                "identity.json",
                "incidents.json",
                "ingest.json",
                "marketplace.json",
                "network.json",
                "patrols.json",
                "storage.json",
                "tenancy.json",
            ],
            sections
        );

        // spot-check content: identity names the requester, tenancy names the org
        using var identity = JsonDocument.Parse(
            new StreamReader(zip.GetEntry("identity.json")!.Open()).ReadToEnd()
        );
        Assert.Contains(
            ApiFixture.UserA,
            identity
                .RootElement.GetProperty("members")
                .EnumerateArray()
                .Select(m => m.GetProperty("email").GetString())
        );
        using var tenancy = JsonDocument.Parse(
            new StreamReader(zip.GetEntry("tenancy.json")!.Open()).ReadToEnd()
        );
        Assert.False(
            string.IsNullOrEmpty(
                tenancy.RootElement.GetProperty("organization").GetProperty("slug").GetString()
            )
        );
    }

    [Fact]
    public async Task Offboarding_is_a_two_step_and_the_platform_org_is_untouchable()
    {
        var op = await fixture.OperatorClient();

        // an ACTIVE org cannot be offboarded - suspend first, deliberately
        var early = await op.PostAsync($"/api/operator/orgs/{fixture.OrgB.Value}/offboard", null);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal(
            "not_suspended",
            (await early.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
        );

        // the platform org is behind the wall in every lifecycle direction
        var platform = await op.PostAsync(
            $"/api/operator/orgs/{fixture.PlatformOrg.Value}/offboard",
            null
        );
        Assert.Equal(HttpStatusCode.BadRequest, platform.StatusCode);
    }

    [Fact]
    public async Task Offboard_purges_the_org_and_locks_everyone_out()
    {
        // a disposable org with real data in several modules
        await fixture.CreateUserOnly("founder@doomed.local");
        var founder = await fixture.LoginAsync("founder@doomed.local");
        var created = await founder.PostAsJsonAsync(
            "/api/orgs",
            new { name = "Doomed Co", slug = "doomed" }
        );
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var orgId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("orgId")
            .GetGuid();
        await ApiFixture.WaitForMembershipAsync(founder);
        (
            await founder.PostAsJsonAsync("/auth/switch-org", new { orgId })
        ).EnsureSuccessStatusCode();
        var hierarchy = await founder.PostAsJsonAsync(
            "/api/hierarchy",
            new { name = "Doomed", levels = new[] { "Region" } }
        );
        hierarchy.EnsureSuccessStatusCode();
        var rootId = (await hierarchy.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("rootNodeId")
            .GetGuid();
        var site = await founder.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Doomed Flagship",
                timeZone = "Etc/UTC",
            }
        );
        site.EnsureSuccessStatusCode();

        // the guest surface is live before the end
        var guest = fixture.GuestClient();
        guest.DefaultRequestHeaders.Add("X-Forwarded-Host", "doomed.locintel.test");
        JsonElement publicSites = default;
        await ApiFixture.WaitUntilAsync(
            async () =>
                (
                    publicSites = await guest.GetFromJsonAsync<JsonElement>("/public/sites")
                ).GetArrayLength() > 0,
            "the site to become publicly visible"
        );
        Assert.Equal(1, publicSites.GetArrayLength());

        // suspend, then offboard
        var op = await fixture.OperatorClient();
        var suspend = await op.PostAsync($"/api/operator/orgs/{orgId}/suspend", null);
        Assert.Equal(HttpStatusCode.NoContent, suspend.StatusCode);
        var offboard = await op.PostAsync($"/api/operator/orgs/{orgId}/offboard", null);
        Assert.Equal(HttpStatusCode.NoContent, offboard.StatusCode);

        // idempotent: the second call is a quiet no-op
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await op.PostAsync($"/api/operator/orgs/{orgId}/offboard", null)).StatusCode
        );

        // the anchor row remains, retired - never a row delete (ADR 25)
        var orgs = await op.GetFromJsonAsync<JsonElement>("/api/operator/orgs");
        var doomed = orgs.EnumerateArray().First(o => o.GetProperty("id").GetGuid() == orgId);
        Assert.Equal("Offboarding", doomed.GetProperty("status").GetString());

        // membership + directory purge: the org vanishes from the founder's world
        JsonElement after = default;
        await ApiFixture.WaitUntilAsync(
            async () =>
                (after = await founder.GetFromJsonAsync<JsonElement>("/me"))
                    .GetProperty("organizations")
                    .GetArrayLength() == 0,
            "the offboarded org to leave the membership read model"
        );
        Assert.Equal(0, after.GetProperty("organizations").GetArrayLength());

        // grants are gone: the scope gate filters reads to nothing (never an
        // error - ADR three-gate), and the grant gate slams writes shut
        Assert.Equal(0, (await ApiFixture.GetItemsAsync(founder, "/api/sites")).GetArrayLength());
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await founder.PostAsJsonAsync(
                    "/api/hierarchy",
                    new { name = "Too late", levels = new[] { "Region" } }
                )
            ).StatusCode
        );

        // and the guest surface answers empty - the host resolves to nothing now
        var publicAfter = await guest.GetFromJsonAsync<JsonElement>("/public/sites");
        Assert.Equal(0, publicAfter.GetArrayLength());
    }
}
