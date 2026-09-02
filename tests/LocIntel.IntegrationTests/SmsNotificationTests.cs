using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Platform.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

/// <summary>
/// SMS rides the org notice: members who opted in to a kind, hold its
/// capability, and have a phone get a text through the local catcher;
/// everyone else gets nothing. The phone must be E.164.
/// </summary>
public class SmsNotificationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Opted_in_members_get_texts_for_alerts_and_marketplace_traffic()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await owner.PutAsJsonAsync(
                    "/api/me/notifications",
                    new { phone = "415-555-1234", smsAlerts = true }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await owner.PutAsJsonAsync(
                    "/api/me/notifications",
                    new { phone = (string?)null, smsAlerts = true }
                )
            ).StatusCode
        );
        var saved = await owner.PutAsJsonAsync(
            "/api/me/notifications",
            new
            {
                phone = "+14155550101",
                smsAlerts = true,
                smsMarketplace = false,
                smsNetwork = false,
                browserAlerts = true,
            }
        );
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var view = await owner.GetFromJsonAsync<JsonElement>("/api/me/notifications");
        Assert.Equal("+14155550101", view.GetProperty("phone").GetString());
        Assert.True(view.GetProperty("smsAvailable").GetBoolean());

        // a critical incident texts the opted-in owner
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
                name = "SMS Store",
                timeZone = "Etc/UTC",
            }
        );
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        (
            await owner.PostAsJsonAsync(
                "/api/incidents",
                new
                {
                    siteId,
                    category = "Robbery",
                    severity = "Critical",
                    title = "Armed robbery",
                    occurredAt = DateTimeOffset.UtcNow,
                }
            )
        ).EnsureSuccessStatusCode();
        var catcher = fixture.Factory.Services.GetRequiredService<LocalSmsCatcher>();
        var got = false;
        for (var i = 0; i < 50 && !got; i++)
        {
            await Task.Delay(100);
            got = catcher.Sent.Any(m =>
                m.To == "+14155550101" && m.Body.Contains("Critical robbery")
            );
        }
        Assert.True(got, "the opted-in owner should have been texted about the critical incident");

        // marketplace kind is off for the owner: a vendor's request submission texts nobody in org A...
        var before = catcher.Sent.Count(m => m.To == "+14155550101");
        var vendor = await fixture.LoginAsync(ApiFixture.UserB);
        (
            await vendor.PutAsJsonAsync(
                "/api/vendor/profile",
                new { name = "SMS Vendor", categories = new[] { "KeyHolding" } }
            )
        ).EnsureSuccessStatusCode();
        (
            await vendor.PostAsJsonAsync("/api/vendor/profile/publish", new { published = true })
        ).EnsureSuccessStatusCode();
        // ...but org B's member opted in to marketplace texts, and gets the dispatch
        (
            await vendor.PutAsJsonAsync(
                "/api/me/notifications",
                new
                {
                    phone = "+14155550202",
                    smsAlerts = false,
                    smsMarketplace = true,
                    smsNetwork = false,
                    browserAlerts = false,
                }
            )
        ).EnsureSuccessStatusCode();
        var request = await owner.PostAsJsonAsync(
            "/api/marketplace/requests",
            new
            {
                vendorOrgId = fixture.OrgB.Value,
                category = "KeyHolding",
                urgency = "Emergency",
                siteId,
                title = "Keys tonight",
                startsAt = DateTimeOffset.UtcNow.AddHours(1),
            }
        );
        var requestId = (await request.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        (
            await owner.PostAsync($"/api/marketplace/requests/{requestId}/submit", null)
        ).EnsureSuccessStatusCode();
        var vendorGot = false;
        for (var i = 0; i < 50 && !vendorGot; i++)
        {
            await Task.Delay(100);
            vendorGot = catcher.Sent.Any(m =>
                m.To == "+14155550202" && m.Body.Contains("Keys tonight")
            );
        }
        Assert.True(vendorGot, "the vendor's opted-in member should have been texted the dispatch");
        await Task.Delay(300);
        Assert.Equal(before, catcher.Sent.Count(m => m.To == "+14155550101"));

        // a role-less member with a phone never gets alert texts (no alerts:read)
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        (
            await viewer.PutAsJsonAsync(
                "/api/me/notifications",
                new
                {
                    phone = "+14155550303",
                    smsAlerts = true,
                    smsMarketplace = false,
                    smsNetwork = false,
                    browserAlerts = false,
                }
            )
        ).EnsureSuccessStatusCode();
        (
            await owner.PostAsJsonAsync(
                "/api/bulletins",
                new
                {
                    kind = "Bolo",
                    severity = "High",
                    title = "Blue van",
                    body = "Watch the lot.",
                }
            )
        ).EnsureSuccessStatusCode();
        var ownerBulletin = false;
        for (var i = 0; i < 50 && !ownerBulletin; i++)
        {
            await Task.Delay(100);
            ownerBulletin = catcher.Sent.Any(m =>
                m.To == "+14155550101" && m.Body.Contains("Blue van")
            );
        }
        Assert.True(ownerBulletin);
        Assert.DoesNotContain(catcher.Sent, m => m.To == "+14155550303");
        // guests hold nothing here
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fixture.GuestClient().GetAsync("/api/me/notifications")).StatusCode
        );
    }
}
