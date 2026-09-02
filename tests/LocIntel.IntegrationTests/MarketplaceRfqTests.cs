using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>Broadcast requests: sent to matching vendors, quoted, awarded by the buyer; then the ordinary fulfillment flow.</summary>
public class MarketplaceRfqTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Broadcast_request_collects_quotes_and_the_award_assigns_the_vendor()
    {
        var vendor = await fixture.LoginAsync(ApiFixture.UserB);
        var buyer = await fixture.LoginAsync(ApiFixture.UserA);
        (
            await vendor.PutAsJsonAsync(
                "/api/vendor/profile",
                new
                {
                    name = "Bravo Board-Up",
                    categories = new[] { "BoardUp", "GuardService" },
                    serviceAreas = new[] { "CA" },
                }
            )
        ).EnsureSuccessStatusCode();
        (
            await vendor.PostAsJsonAsync("/api/vendor/profile/publish", new { published = true })
        ).EnsureSuccessStatusCode();

        var hierarchy = await buyer.GetAsync("/api/hierarchy");
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
            var created = await buyer.PostAsJsonAsync(
                "/api/hierarchy",
                new { name = "Org A", levels = new[] { "Region" } }
            );
            created.EnsureSuccessStatusCode();
            rootId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("rootNodeId")
                .GetGuid();
        }
        var site = await buyer.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "RFQ Store",
                timeZone = "America/Los_Angeles",
            }
        );
        site.EnsureSuccessStatusCode();
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        // broadcast: no vendor chosen; a category nobody offers has nobody to send to
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await buyer.PostAsJsonAsync(
                    "/api/marketplace/requests",
                    new
                    {
                        category = "LegalSupport",
                        urgency = "Scheduled",
                        siteId,
                        title = "Nobody offers this",
                        startsAt = DateTimeOffset.UtcNow.AddDays(2),
                    }
                )
            ).StatusCode
        );
        var created2 = await buyer.PostAsJsonAsync(
            "/api/marketplace/requests",
            new
            {
                category = "BoardUp",
                urgency = "Emergency",
                siteId,
                title = "Board the front window tonight",
                details = "Glass out at the entrance.",
                startsAt = DateTimeOffset.UtcNow.AddHours(2),
                budgetAmount = 1500,
            }
        );
        Assert.True(created2.IsSuccessStatusCode, await created2.Content.ReadAsStringAsync());
        var requestId = (await created2.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var draft = await buyer.GetFromJsonAsync<JsonElement>(
            $"/api/marketplace/requests/{requestId}"
        );
        Assert.Equal("Broadcast", draft.GetProperty("mode").GetString());
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("vendorOrgId").ValueKind);

        // the vendor sees nothing until submission; then it is in their queue as a recipient
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await vendor.GetAsync($"/api/vendor/requests/{requestId}")).StatusCode
        );
        (
            await buyer.PostAsync($"/api/marketplace/requests/{requestId}/submit", null)
        ).EnsureSuccessStatusCode();
        var queue = await vendor.GetFromJsonAsync<JsonElement>("/api/vendor/requests");
        Assert.Contains(
            queue.GetProperty("items").EnumerateArray(),
            r => r.GetProperty("id").GetGuid() == requestId
        );
        var seen = await vendor.GetFromJsonAsync<JsonElement>($"/api/vendor/requests/{requestId}");
        Assert.Equal("Submitted", seen.GetProperty("status").GetString());
        // a recipient cannot accept a broadcast outright - the buyer awards it
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await vendor.PostAsync($"/api/vendor/requests/{requestId}/accept", null)).StatusCode
        );

        var quoted = await vendor.PostAsJsonAsync(
            $"/api/vendor/requests/{requestId}/quotes",
            new
            {
                amount = 1250,
                notes = "Crew can be there in 90 minutes.",
                validUntil = DateTimeOffset.UtcNow.AddDays(1),
            }
        );
        Assert.True(quoted.IsSuccessStatusCode, await quoted.Content.ReadAsStringAsync());
        var quoteId = (await quoted.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        // one quote per vendor: a second submission replaces the amount
        (
            await vendor.PostAsJsonAsync(
                $"/api/vendor/requests/{requestId}/quotes",
                new { amount = 1200 }
            )
        ).EnsureSuccessStatusCode();

        var detail = await buyer.GetFromJsonAsync<JsonElement>(
            $"/api/marketplace/requests/{requestId}"
        );
        var quote = Assert.Single(detail.GetProperty("quotes").EnumerateArray());
        Assert.Equal(1200m, quote.GetProperty("amount").GetDecimal());
        Assert.Equal("Bravo Board-Up", quote.GetProperty("vendorName").GetString());
        Assert.Single(detail.GetProperty("recipients").EnumerateArray());
        Assert.Equal(
            "Quoted",
            detail.GetProperty("recipients")[0].GetProperty("status").GetString()
        );

        // the vendor cannot award their own quote; the buyer does
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await vendor.PostAsync(
                    $"/api/marketplace/requests/{requestId}/quotes/{quoteId}/accept",
                    null
                )
            ).StatusCode
        );
        (
            await buyer.PostAsync(
                $"/api/marketplace/requests/{requestId}/quotes/{quoteId}/accept",
                null
            )
        ).EnsureSuccessStatusCode();
        detail = await buyer.GetFromJsonAsync<JsonElement>(
            $"/api/marketplace/requests/{requestId}"
        );
        Assert.Equal("Accepted", detail.GetProperty("status").GetString());
        Assert.Equal(fixture.OrgB.Value, detail.GetProperty("vendorOrgId").GetGuid());
        Assert.Equal(1200m, detail.GetProperty("budgetAmount").GetDecimal());
        Assert.Equal("Accepted", detail.GetProperty("quotes")[0].GetProperty("status").GetString());

        // from here the ordinary flow: the awarded vendor starts and completes, the buyer verifies
        (
            await vendor.PostAsync($"/api/vendor/requests/{requestId}/start", null)
        ).EnsureSuccessStatusCode();
        (
            await vendor.PostAsJsonAsync(
                $"/api/vendor/requests/{requestId}/complete",
                new { summary = "Boarded and swept." }
            )
        ).EnsureSuccessStatusCode();
        (
            await buyer.PostAsync($"/api/marketplace/requests/{requestId}/verify", null)
        ).EnsureSuccessStatusCode();

        // a third org sees neither side; declining a broadcast is per recipient
        var third = await fixture.OperatorClient();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await third.GetAsync($"/api/vendor/requests/{requestId}")).StatusCode
        );
        var another = await buyer.PostAsJsonAsync(
            "/api/marketplace/requests",
            new
            {
                category = "GuardService",
                urgency = "Scheduled",
                siteId,
                title = "Weekend guard",
                startsAt = DateTimeOffset.UtcNow.AddDays(3),
            }
        );
        var anotherId = (await another.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        (
            await buyer.PostAsync($"/api/marketplace/requests/{anotherId}/submit", null)
        ).EnsureSuccessStatusCode();
        (
            await vendor.PostAsJsonAsync(
                $"/api/vendor/requests/{anotherId}/decline",
                new { reason = "No coverage that weekend" }
            )
        ).EnsureSuccessStatusCode();
        var declined = await buyer.GetFromJsonAsync<JsonElement>(
            $"/api/marketplace/requests/{anotherId}"
        );
        Assert.Equal("Submitted", declined.GetProperty("status").GetString()); // still open for other vendors
        Assert.Equal(
            "Declined",
            declined.GetProperty("recipients")[0].GetProperty("status").GetString()
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await vendor.GetAsync($"/api/vendor/requests/{anotherId}")).StatusCode
        );
    }
}
