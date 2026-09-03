using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Cross-org intelligence sharing: the owner invites by slug, the invitee's
/// own access row arrives under THEIR tenant, bulletins are copies readable
/// by active members only, and importing one creates a local record.
/// </summary>
public class NetworkTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Share_invite_publish_import_and_leave()
    {
        var a = await fixture.LoginAsync(ApiFixture.UserA);
        var b = await fixture.LoginAsync(ApiFixture.UserB);

        var created = await a.PostAsJsonAsync(
            "/api/network/shares",
            new { name = "Regional ORC Network", description = "Retailers in the metro." }
        );
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var shareId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        // org B knows nothing until invited; a bad slug is a 404
        var none = await b.GetFromJsonAsync<JsonElement>("/api/network/shares");
        Assert.Empty(none.GetProperty("items").EnumerateArray());
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await a.PostAsJsonAsync(
                    $"/api/network/shares/{shareId}/invite",
                    new { slug = "nobody" }
                )
            ).StatusCode
        );
        (
            await a.PostAsJsonAsync($"/api/network/shares/{shareId}/invite", new { slug = "ORG-B" })
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await a.PostAsJsonAsync(
                    $"/api/network/shares/{shareId}/invite",
                    new { slug = "org-b" }
                )
            ).StatusCode
        );

        // the invitee's access row is written under their own tenant, asynchronously
        JsonElement mine = default;
        var invited = false;
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                mine = await b.GetFromJsonAsync<JsonElement>("/api/network/shares");
                invited = mine.GetProperty("items")
                    .EnumerateArray()
                    .Any(s => s.GetProperty("id").GetGuid() == shareId);
                return !(!invited);
            },
            "invited"
        );
        Assert.True(invited, "org B should see the invitation");
        var pending = mine.GetProperty("items")
            .EnumerateArray()
            .Single(s => s.GetProperty("id").GetGuid() == shareId);
        Assert.Equal("Invited", pending.GetProperty("membership").GetString());
        Assert.False(pending.GetProperty("owned").GetBoolean());

        // org A publishes a BOLO from one of its own records
        var entity = await a.PostAsJsonAsync(
            "/api/entities",
            new
            {
                kind = "Person",
                displayName = "Blue Cap",
                aliases = new[] { "Cap" },
                descriptors = new Dictionary<string, string> { ["height"] = "5'9\"" },
            }
        );
        entity.EnsureSuccessStatusCode();
        var entityId = (await entity.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var published = await a.PostAsJsonAsync(
            $"/api/network/shares/{shareId}/bulletins",
            new
            {
                kind = "Bolo",
                severity = "High",
                title = "Blue Cap crew",
                body = "Hits cosmetics at close.",
                entityId,
                areas = new[] { "ca" },
            }
        );
        Assert.True(published.IsSuccessStatusCode, await published.Content.ReadAsStringAsync());
        var bulletinId = (await published.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        // invited-but-not-accepted sees the share but not its bulletins, and cannot publish
        var before = await b.GetFromJsonAsync<JsonElement>(
            $"/api/network/bulletins?shareId={shareId}"
        );
        Assert.Equal(0, before.GetProperty("total").GetInt32());
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await b.PostAsJsonAsync(
                    $"/api/network/shares/{shareId}/bulletins",
                    new
                    {
                        kind = "Advisory",
                        severity = "Low",
                        title = "x",
                        body = "y",
                    }
                )
            ).StatusCode
        );
        (
            await b.PostAsync($"/api/network/shares/{shareId}/accept", null)
        ).EnsureSuccessStatusCode();

        // the owner records the join and republishes the roster to every member
        await ApiFixture.WaitUntilAsync(
            async () =>
                (await b.GetFromJsonAsync<JsonElement>($"/api/network/shares/{shareId}"))
                    .GetProperty("members")
                    .EnumerateArray()
                    .Count(m => m.GetProperty("status").GetString() == "Active") == 2,
            "the roster to reach the new member"
        );
        // ...and re-offers what was published before the member joined: its own copy
        JsonElement seen = default;
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                seen = await b.GetFromJsonAsync<JsonElement>(
                    $"/api/network/bulletins?shareId={shareId}"
                );
                return seen.GetProperty("total").GetInt32() == 1;
            },
            "the earlier bulletin's copy to reach the new member"
        );
        var copy = Assert.Single(seen.GetProperty("items").EnumerateArray());
        Assert.Equal("Blue Cap", copy.GetProperty("displayName").GetString());
        Assert.Equal("5'9\"", copy.GetProperty("descriptors").GetProperty("height").GetString());
        Assert.False(copy.GetProperty("mine").GetBoolean());
        Assert.Equal(
            new[] { "CA" },
            copy.GetProperty("areas").EnumerateArray().Select(x => x.GetString()).ToArray()
        );
        // search reaches aliases too
        var found = await b.GetFromJsonAsync<JsonElement>("/api/network/bulletins?q=cap");
        Assert.Contains(
            found.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("id").GetGuid() == bulletinId
        );

        // only the publisher withdraws; B imports it as its own Suspected record
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await b.PostAsync($"/api/network/bulletins/{bulletinId}/withdraw", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await a.PostAsync($"/api/network/bulletins/{bulletinId}/import", null)).StatusCode
        );
        (
            await b.PostAsync($"/api/network/bulletins/{bulletinId}/import", null)
        ).EnsureSuccessStatusCode();
        var imported = false;
        JsonElement bEntities = default;
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                bEntities = await b.GetFromJsonAsync<JsonElement>("/api/entities?q=Blue%20Cap");
                imported = bEntities.GetProperty("total").GetInt32() > 0;
                return !(!imported);
            },
            "imported"
        );
        Assert.True(imported, "the import should have created a record in org B");
        var local = bEntities.GetProperty("items")[0];
        Assert.Equal("Suspected", local.GetProperty("status").GetString());
        Assert.NotEqual(entityId, local.GetProperty("id").GetGuid());
        var localDetail = await b.GetFromJsonAsync<JsonElement>(
            $"/api/entities/{local.GetProperty("id").GetGuid()}"
        );
        Assert.Contains("Regional ORC Network", localDetail.GetProperty("summary").GetString());

        // a third org sees nothing; gate 1 blocks creating shares without the plan
        var third = await fixture.OperatorClient();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await third.GetAsync($"/api/network/shares/{shareId}")).StatusCode
        );
        var op = await fixture.OperatorClient();
        (
            await op.PutAsJsonAsync(
                $"/api/operator/orgs/{fixture.OrgB.Value}/entitlements/network.enabled",
                new { value = "false" }
            )
        ).EnsureSuccessStatusCode();
        try
        {
            var blocked = await b.PostAsJsonAsync("/api/network/shares", new { name = "Nope" });
            Assert.Equal(HttpStatusCode.PaymentRequired, blocked.StatusCode);
        }
        finally
        {
            (
                await op.PutAsJsonAsync(
                    $"/api/operator/orgs/{fixture.OrgB.Value}/entitlements/network.enabled",
                    new { value = "true" }
                )
            ).EnsureSuccessStatusCode();
        }

        // withdraw hides it; leaving hides the share's bulletins entirely
        (
            await a.PostAsync($"/api/network/bulletins/{bulletinId}/withdraw", null)
        ).EnsureSuccessStatusCode();
        await ApiFixture.WaitUntilAsync(
            async () =>
                (await b.GetFromJsonAsync<JsonElement>($"/api/network/bulletins?shareId={shareId}"))
                    .GetProperty("total")
                    .GetInt32() == 0,
            "the withdrawal to reach the member's copy"
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await a.PostAsync($"/api/network/shares/{shareId}/leave", null)).StatusCode
        );
        (await b.PostAsync($"/api/network/shares/{shareId}/leave", null)).EnsureSuccessStatusCode();
        await ApiFixture.WaitUntilAsync(
            async () =>
                (await a.GetFromJsonAsync<JsonElement>($"/api/network/shares/{shareId}"))
                    .GetProperty("members")
                    .EnumerateArray()
                    .Any(m =>
                        m.GetProperty("orgId").GetGuid() == fixture.OrgB.Value
                        && m.GetProperty("status").GetString() == "Left"
                    ),
            "the departure to reach the owner's roster"
        );

        // guests and role-less members hold nothing
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fixture.GuestClient().GetAsync("/api/network/shares")).StatusCode
        );
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.GetAsync("/api/network/bulletins")).StatusCode
        );
    }
}
