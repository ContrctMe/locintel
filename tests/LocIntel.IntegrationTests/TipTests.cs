using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The guest tier's one write (ADR 7): a visitor at the org's public host
/// leaves a tip that lands as an incident with Source = Tip. Nothing about
/// the org's incidents flows back; an unknown host gets nothing at all.
/// </summary>
public class TipTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Guest_tip_becomes_an_incident_for_the_host_org()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var hierarchy = await owner.GetAsync("/api/hierarchy");
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
            var created = await owner.PostAsJsonAsync(
                "/api/hierarchy",
                new { name = "Org A", levels = new[] { "Region" } }
            );
            created.EnsureSuccessStatusCode();
            rootId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("rootNodeId")
                .GetGuid();
        }
        var site = await owner.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Tip Store",
                timeZone = "America/Chicago",
            }
        );
        site.EnsureSuccessStatusCode();
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var guest = fixture.GuestClient();
        guest.DefaultRequestHeaders.Add("X-Forwarded-Host", "org-a.locintel.test");

        // too short, then real
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await guest.PostAsJsonAsync(
                    "/public/tips",
                    new
                    {
                        siteId,
                        category = "Theft",
                        description = "short",
                    }
                )
            ).StatusCode
        );
        var sent = await guest.PostAsJsonAsync(
            "/public/tips",
            new
            {
                siteId,
                category = "OrganizedRetailCrime",
                description = "Three people filled bags in cosmetics and walked out the garden exit around 6pm.",
                contact = "tipster@example.test",
            }
        );
        Assert.True(sent.IsSuccessStatusCode, await sent.Content.ReadAsStringAsync());
        var receipt = (await sent.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("receipt")
            .GetString();
        Assert.StartsWith("TIP-", receipt);

        // the honeypot swallows bots with the same receipt shape and no row
        var bot = await guest.PostAsJsonAsync(
            "/public/tips",
            new
            {
                siteId,
                category = "Theft",
                description = "BUY CHEAP WATCHES NOW ONLINE",
                website = "http://spam.example",
            }
        );
        bot.EnsureSuccessStatusCode();

        // the org sees exactly one tip, as an incident, with the contact
        var incidents = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/incidents?siteId={siteId}"
        );
        var tip = Assert.Single(incidents.GetProperty("items").EnumerateArray());
        Assert.Equal("OrganizedRetailCrime", tip.GetProperty("category").GetString());
        var detail = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/incidents/{tip.GetProperty("id").GetGuid()}"
        );
        Assert.Equal("Tip", detail.GetProperty("source").GetString());
        Assert.Equal("tipster@example.test", detail.GetProperty("reporterContact").GetString());
        Assert.StartsWith("Tip: Three people", detail.GetProperty("title").GetString());
        Assert.Contains(
            "tip",
            detail.GetProperty("tags").EnumerateArray().Select(t => t.GetString())
        );

        // an unknown host is nobody's guest: 404, never a hint
        var nowhere = fixture.GuestClient();
        nowhere.DefaultRequestHeaders.Add("X-Forwarded-Host", "nobody.locintel.test");
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await nowhere.PostAsJsonAsync(
                    "/public/tips",
                    new
                    {
                        siteId,
                        category = "Theft",
                        description = "Something happened here today.",
                    }
                )
            ).StatusCode
        );
        // and another org's site id is not this org's site
        var elsewhere = fixture.GuestClient();
        elsewhere.DefaultRequestHeaders.Add("X-Forwarded-Host", "org-b.locintel.test");
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await elsewhere.PostAsJsonAsync(
                    "/public/tips",
                    new
                    {
                        siteId,
                        category = "Theft",
                        description = "Something happened here today.",
                    }
                )
            ).StatusCode
        );
    }
}
