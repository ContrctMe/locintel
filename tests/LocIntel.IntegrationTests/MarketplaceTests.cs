using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The two-party tenancy model: org B publishes a vendor profile, org A
/// hires it; each side reads its own half through its own endpoints and
/// RLS admits either org to the shared rows. Gate 1 (marketplace.enabled)
/// is a real 402 here.
/// </summary>
public class MarketplaceTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Direct_request_round_trip_across_two_orgs()
    {
        var vendor = await fixture.LoginAsync(ApiFixture.UserB);
        var buyer = await fixture.LoginAsync(ApiFixture.UserA);

        // org B becomes a vendor: profile, credential, publish
        var profile = await vendor.PutAsJsonAsync(
            "/api/vendor/profile",
            new
            {
                name = "Bravo Guard Services",
                description = "Armed and unarmed guards, 24/7.",
                categories = new[] { "GuardService", "MobilePatrol" },
                serviceAreas = new[] { "ca", "NV" },
                latitude = 34.05,
                longitude = -118.25,
                serviceRadiusKm = 150,
                contactEmail = "dispatch@bravo.test",
            }
        );
        Assert.True(profile.IsSuccessStatusCode, await profile.Content.ReadAsStringAsync());
        (
            await vendor.PostAsJsonAsync(
                "/api/vendor/credentials",
                new
                {
                    kind = "License",
                    label = "CA PPO",
                    number = "PPO-12345",
                    jurisdiction = "ca",
                    expiresAt = DateTimeOffset.UtcNow.AddYears(1),
                }
            )
        ).EnsureSuccessStatusCode();
        // unpublished: invisible to buyers (explicitly, so test order cannot matter)
        (
            await vendor.PostAsJsonAsync("/api/vendor/profile/publish", new { published = false })
        ).EnsureSuccessStatusCode();
        await ApiFixture.WaitUntilAsync(
            async () =>
                !(await buyer.GetFromJsonAsync<JsonElement>("/api/marketplace/vendors"))
                    .GetProperty("items")
                    .EnumerateArray()
                    .Any(v => v.GetProperty("orgId").GetGuid() == fixture.OrgB.Value),
            "the unpublished vendor to leave the directory"
        );
        (
            await vendor.PostAsJsonAsync("/api/vendor/profile/publish", new { published = true })
        ).EnsureSuccessStatusCode();

        // the catalog is a projection: it crosses the org line through the outbox, credentials included
        JsonElement catalog = default;
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                catalog = await buyer.GetFromJsonAsync<JsonElement>(
                    "/api/marketplace/vendors?category=GuardService&area=ca"
                );
                return catalog
                    .GetProperty("items")
                    .EnumerateArray()
                    .Any(v =>
                        v.GetProperty("orgId").GetGuid() == fixture.OrgB.Value
                        && v.GetProperty("validCredentials").GetInt32() == 1
                    );
            },
            "the published vendor and its credential to reach the directory"
        );
        var listed = Assert.Single(
            catalog.GetProperty("items").EnumerateArray(),
            v => v.GetProperty("orgId").GetGuid() == fixture.OrgB.Value
        );
        Assert.Equal("Bravo Guard Services", listed.GetProperty("name").GetString());
        Assert.Equal(1, listed.GetProperty("validCredentials").GetInt32());
        Assert.Equal(
            new[] { "CA", "NV" },
            listed.GetProperty("serviceAreas").EnumerateArray().Select(a => a.GetString()).ToArray()
        );
        var vendorDetail = await buyer.GetFromJsonAsync<JsonElement>(
            $"/api/marketplace/vendors/{fixture.OrgB.Value}"
        );
        Assert.Single(vendorDetail.GetProperty("credentials").EnumerateArray());
        (
            await buyer.PostAsJsonAsync(
                "/api/marketplace/preferred",
                new
                {
                    vendorOrgId = fixture.OrgB.Value,
                    categories = new[] { "GuardService" },
                    notes = "Fast response",
                }
            )
        ).EnsureSuccessStatusCode();

        // org A raises a guard request at a site with coordinates
        var siteId = await SiteAsync(buyer, "Downtown Store", 34.0522, -118.2437);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await buyer.PostAsJsonAsync(
                    "/api/marketplace/requests",
                    new
                    {
                        vendorOrgId = fixture.OrgB.Value,
                        category = "CctvInstall",
                        urgency = "Scheduled",
                        siteId,
                        title = "Not offered",
                        startsAt = DateTimeOffset.UtcNow.AddDays(1),
                    }
                )
            ).StatusCode
        );
        var created = await buyer.PostAsJsonAsync(
            "/api/marketplace/requests",
            new
            {
                vendorOrgId = fixture.OrgB.Value,
                category = "GuardService",
                urgency = "Emergency",
                siteId,
                title = "Overnight guard after break-in",
                details = "Front glass boarded; need presence until repair.",
                spec = new Dictionary<string, string> { ["headcount"] = "1", ["armed"] = "no" },
                startsAt = DateTimeOffset.UtcNow.AddHours(1),
                endsAt = DateTimeOffset.UtcNow.AddHours(13),
                budgetAmount = 900,
            }
        );
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var requestId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        // drafts never reach the vendor
        var queue = await vendor.GetFromJsonAsync<JsonElement>("/api/vendor/requests");
        Assert.DoesNotContain(
            queue.GetProperty("items").EnumerateArray(),
            r => r.GetProperty("id").GetGuid() == requestId
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await vendor.GetAsync($"/api/vendor/requests/{requestId}")).StatusCode
        );
        await TransitionOnceAsync(buyer, $"/api/marketplace/requests/{requestId}/submit");

        // now it does - as the vendor's OWN row, materialized through the outbox;
        // the vendor sees the requester's name and the site snapshot
        await ApiFixture.WaitUntilAsync(
            async () =>
                (await vendor.GetAsync($"/api/vendor/requests/{requestId}")).IsSuccessStatusCode,
            "the vendor's assignment row to arrive"
        );
        var incoming = await vendor.GetFromJsonAsync<JsonElement>(
            $"/api/vendor/requests/{requestId}"
        );
        Assert.Equal("Submitted", incoming.GetProperty("status").GetString());
        Assert.Equal("Downtown Store", incoming.GetProperty("siteName").GetString());
        Assert.Equal("1", incoming.GetProperty("spec").GetProperty("headcount").GetString());
        Assert.False(string.IsNullOrEmpty(incoming.GetProperty("requesterName").GetString()));
        // and the sides are sealed: org B cannot use the buyer's endpoints on it, nor A the vendor's
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await vendor.GetAsync($"/api/marketplace/requests/{requestId}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await buyer.PostAsync($"/api/vendor/requests/{requestId}/accept", null)).StatusCode
        );

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await vendor.PostAsync($"/api/vendor/requests/{requestId}/start", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await vendor.PostAsJsonAsync(
                    $"/api/vendor/requests/{requestId}/complete",
                    new { summary = "Too early" }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await buyer.PostAsync($"/api/marketplace/requests/{requestId}/verify", null)
            ).StatusCode
        );
        await TransitionOnceAsync(vendor, $"/api/vendor/requests/{requestId}/accept");
        var checkIn = await vendor.PostAsJsonAsync(
            $"/api/vendor/requests/{requestId}/check-in",
            new
            {
                latitude = 34.0525,
                longitude = -118.2440,
                note = "On post",
            }
        );
        checkIn.EnsureSuccessStatusCode();
        var position = await checkIn.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(position.GetProperty("withinGeofence").GetBoolean());
        Assert.True(position.GetProperty("distanceFromSiteMeters").GetDouble() < 100);
        await TransitionOnceAsync(vendor, $"/api/vendor/requests/{requestId}/start");
        var far = await vendor.PostAsJsonAsync(
            $"/api/vendor/requests/{requestId}/check-out",
            new { latitude = 34.20, longitude = -118.50 }
        );
        Assert.False(
            (await far.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("withinGeofence")
                .GetBoolean()
        );
        (
            await vendor.PostAsJsonAsync(
                $"/api/vendor/requests/{requestId}/messages",
                new { body = "Relief arrives at 06:00." }
            )
        ).EnsureSuccessStatusCode();
        await TransitionOnceAsync(
            vendor,
            $"/api/vendor/requests/{requestId}/complete",
            new { summary = "Quiet night." }
        );

        // the buyer sees the whole timeline with sides (its own copies, one per
        // entry the vendor made), and verifies
        JsonElement detail = default;
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                detail = await buyer.GetFromJsonAsync<JsonElement>(
                    $"/api/marketplace/requests/{requestId}"
                );
                return detail.GetProperty("status").GetString() == "Completed"
                    && detail.GetProperty("events").GetArrayLength() == 7;
            },
            "the vendor's work to reach the buyer's request"
        );
        Assert.Equal("Bravo Guard Services", detail.GetProperty("vendorName").GetString());
        var kinds = detail
            .GetProperty("events")
            .EnumerateArray()
            .Select(e => e.GetProperty("kind").GetString())
            .ToList();
        Assert.Equal(
            [
                "StatusChange",
                "StatusChange",
                "CheckIn",
                "StatusChange",
                "CheckOut",
                "Message",
                "StatusChange",
            ],
            kinds
        );
        var vendorMessage = detail
            .GetProperty("events")
            .EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "Message");
        Assert.Equal("vendor", vendorMessage.GetProperty("side").GetString());
        Assert.Equal(JsonValueKind.Null, vendorMessage.GetProperty("actor").ValueKind); // never the other org's people
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await buyer.PostAsJsonAsync(
                    $"/api/marketplace/requests/{requestId}/cancel",
                    new { reason = "Already completed" }
                )
            ).StatusCode
        );
        await TransitionOnceAsync(buyer, $"/api/marketplace/requests/{requestId}/verify");
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await buyer.PostAsJsonAsync(
                    $"/api/marketplace/requests/{requestId}/dispute",
                    new { reason = "Already verified" }
                )
            ).StatusCode
        );
        var list = await buyer.GetFromJsonAsync<JsonElement>(
            $"/api/marketplace/requests?siteId={siteId}&status=Verified"
        );
        Assert.Contains(
            list.GetProperty("items").EnumerateArray(),
            r => r.GetProperty("id").GetGuid() == requestId
        );

        // gate 1: without the plan feature, raising is a 402 with an upsell code
        var op = await fixture.OperatorClient();
        (
            await op.PutAsJsonAsync(
                $"/api/operator/orgs/{fixture.OrgA.Value}/entitlements/marketplace.enabled",
                new { value = "false" }
            )
        ).EnsureSuccessStatusCode();
        try
        {
            var blocked = await buyer.PostAsJsonAsync(
                "/api/marketplace/requests",
                new
                {
                    vendorOrgId = fixture.OrgB.Value,
                    category = "GuardService",
                    urgency = "Scheduled",
                    siteId,
                    title = "Blocked",
                    startsAt = DateTimeOffset.UtcNow.AddDays(1),
                }
            );
            Assert.Equal(HttpStatusCode.PaymentRequired, blocked.StatusCode);
            Assert.Equal(
                "marketplace.enabled",
                (await blocked.Content.ReadFromJsonAsync<JsonElement>())
                    .GetProperty("code")
                    .GetString()
            );
        }
        finally
        {
            (
                await op.PutAsJsonAsync(
                    $"/api/operator/orgs/{fixture.OrgA.Value}/entitlements/marketplace.enabled",
                    new { value = "true" }
                )
            ).EnsureSuccessStatusCode();
        }

        // guests and role-less members hold nothing on either side
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fixture.GuestClient().GetAsync("/api/marketplace/vendors")).StatusCode
        );
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.GetAsync("/api/marketplace/requests")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.GetAsync("/api/vendor/profile")).StatusCode
        );
    }

    [Fact]
    public async Task Expired_credentials_block_new_assignments_and_blocked_vendors_cannot_be_hired()
    {
        var vendor = await fixture.LoginAsync(ApiFixture.UserB);
        var buyer = await fixture.LoginAsync(ApiFixture.UserA);
        (
            await vendor.PutAsJsonAsync(
                "/api/vendor/profile",
                new
                {
                    name = "Bravo Guard Services",
                    categories = new[] { "GuardService", "BoardUp" },
                }
            )
        ).EnsureSuccessStatusCode();
        (
            await vendor.PostAsJsonAsync("/api/vendor/profile/publish", new { published = true })
        ).EnsureSuccessStatusCode();
        await ApiFixture.WaitUntilAsync(
            async () =>
                (await buyer.GetFromJsonAsync<JsonElement>("/api/marketplace/vendors"))
                    .GetProperty("items")
                    .EnumerateArray()
                    .Any(v => v.GetProperty("orgId").GetGuid() == fixture.OrgB.Value),
            "the published vendor to reach the directory"
        );
        var expired = await vendor.PostAsJsonAsync(
            "/api/vendor/credentials",
            new
            {
                kind = "Insurance",
                label = "GL policy",
                expiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            }
        );
        expired.EnsureSuccessStatusCode();
        var expiredId = (await expired.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var siteId = await SiteAsync(buyer, "Credential Store", null, null);
        var created = await buyer.PostAsJsonAsync(
            "/api/marketplace/requests",
            new
            {
                vendorOrgId = fixture.OrgB.Value,
                category = "BoardUp",
                urgency = "Scheduled",
                siteId,
                title = "Board the side window",
                startsAt = DateTimeOffset.UtcNow.AddDays(1),
            }
        );
        created.EnsureSuccessStatusCode();
        var requestId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        (
            await buyer.PostAsync($"/api/marketplace/requests/{requestId}/submit", null)
        ).EnsureSuccessStatusCode();

        await ApiFixture.WaitUntilAsync(
            async () =>
                (await vendor.GetAsync($"/api/vendor/requests/{requestId}")).IsSuccessStatusCode,
            "the vendor's assignment row to arrive"
        );
        var refused = await vendor.PostAsync($"/api/vendor/requests/{requestId}/accept", null);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        (
            await vendor.DeleteAsync($"/api/vendor/credentials/{expiredId}")
        ).EnsureSuccessStatusCode();
        (
            await vendor.PostAsync($"/api/vendor/requests/{requestId}/accept", null)
        ).EnsureSuccessStatusCode();
        // a check-in without site coordinates records position but no distance
        var checkIn = await vendor.PostAsJsonAsync(
            $"/api/vendor/requests/{requestId}/check-in",
            new { latitude = 1.0, longitude = 1.0 }
        );
        Assert.Equal(
            JsonValueKind.Null,
            (await checkIn.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("withinGeofence")
                .ValueKind
        );
        // the buyer can still cancel accepted work, with a reason
        (
            await buyer.PostAsJsonAsync(
                $"/api/marketplace/requests/{requestId}/cancel",
                new { reason = "Window replaced early" }
            )
        ).EnsureSuccessStatusCode();
        await ApiFixture.WaitUntilAsync(
            async () =>
                (await vendor.GetFromJsonAsync<JsonElement>($"/api/vendor/requests/{requestId}"))
                    .GetProperty("status")
                    .GetString() == "Cancelled",
            "the cancellation to reach the vendor's row"
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await vendor.PostAsync($"/api/vendor/requests/{requestId}/start", null)).StatusCode
        );

        // block the vendor: no new requests to them
        (
            await buyer.PostAsJsonAsync(
                "/api/marketplace/preferred",
                new { vendorOrgId = fixture.OrgB.Value, blocked = true }
            )
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await buyer.PostAsJsonAsync(
                    "/api/marketplace/requests",
                    new
                    {
                        vendorOrgId = fixture.OrgB.Value,
                        category = "BoardUp",
                        urgency = "Scheduled",
                        siteId,
                        title = "Blocked vendor",
                        startsAt = DateTimeOffset.UtcNow.AddDays(1),
                    }
                )
            ).StatusCode
        );
        var preferred = await buyer.GetFromJsonAsync<JsonElement>("/api/marketplace/preferred");
        var row = Assert.Single(
            preferred.EnumerateArray(),
            p => p.GetProperty("vendorOrgId").GetGuid() == fixture.OrgB.Value
        );
        (
            await buyer.DeleteAsync($"/api/marketplace/preferred/{row.GetProperty("id").GetGuid()}")
        ).EnsureSuccessStatusCode();
    }

    private static async Task TransitionOnceAsync(
        HttpClient client,
        string path,
        object? body = null
    )
    {
        var attempts = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(_ =>
                    body is null ? client.PostAsync(path, null) : client.PostAsJsonAsync(path, body)
                )
        );
        var accepted = Assert.Single(
            attempts,
            response => response.StatusCode == HttpStatusCode.OK
        );
        Assert.All(
            attempts.Where(response => response != accepted),
            response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode)
        );
        foreach (var response in attempts)
            response.Dispose();
    }

    private static async Task<Guid> SiteAsync(
        HttpClient client,
        string name,
        double? lat,
        double? lng
    )
    {
        var hierarchy = await client.GetAsync("/api/hierarchy");
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
            var created = await client.PostAsJsonAsync(
                "/api/hierarchy",
                new { name = "Org A", levels = new[] { "Region" } }
            );
            created.EnsureSuccessStatusCode();
            rootId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("rootNodeId")
                .GetGuid();
        }
        var site = await client.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name,
                timeZone = "America/Los_Angeles",
                latitude = lat,
                longitude = lng,
            }
        );
        Assert.True(site.IsSuccessStatusCode, await site.Content.ReadAsStringAsync());
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        return siteId;
    }
}
