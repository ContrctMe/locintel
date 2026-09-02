using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The guest journey (ADR 7): a browser at {org-slug}.domain sees that org's
/// public sites and hours - no login, no grants, same gates.
/// </summary>
public class PublicJourneyTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private HttpClient GuestFor(string slug)
    {
        var client = fixture.GuestClient();
        // what the SSR/public app forwards from the browser's address bar
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", $"{slug}.locintel.test");
        return client;
    }

    private async Task<(Guid open, Guid closed)> SeedSites()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        // the fixture helper is idempotent, which is what keeps three tests
        // calling this from creating duplicate "Public Open Store" rows
        Task<Guid> Site(string name) => ApiFixture.EnsureSiteAsync(owner, name);

        var open = await Site("Public Open Store");
        var closed = await Site("Public Closed Store");
        (
            await owner.PostAsJsonAsync($"/api/sites/{closed}", new { status = "Closed" })
        ).EnsureSuccessStatusCode();

        // hours so openNow can be true: daily, all day, UTC
        (
            await owner.PostAsJsonAsync(
                $"/api/sites/{open}/schedules",
                new
                {
                    name = "Always",
                    rrule = "FREQ=DAILY",
                    anchorDate = "2026-01-01",
                    opens = "00:00",
                    closes = "23:59",
                }
            )
        ).EnsureSuccessStatusCode();
        await ApiFixture.WaitUntilAsync(
            async () => (await fixture.QueryWindows(open)).Count > 0,
            "the open site's occurrence windows to be projected"
        );
        return (open, closed);
    }

    [Fact]
    public async Task Guest_at_org_host_sees_public_sites_with_open_now()
    {
        var (openId, closedId) = await SeedSites();
        var guest = GuestFor("org-a");

        var sites = await guest.GetFromJsonAsync<JsonElement>("/public/sites");
        var names = sites
            .EnumerateArray()
            .ToDictionary(s => s.GetProperty("name").GetString()!, s => s);
        Assert.True(names.ContainsKey("Public Open Store"));
        Assert.False(names.ContainsKey("Public Closed Store")); // closed: invisible
        Assert.True(names["Public Open Store"].GetProperty("openNow").GetBoolean());

        var detail = await guest.GetFromJsonAsync<JsonElement>($"/public/sites/{openId}");
        Assert.Equal("Etc/UTC", detail.GetProperty("timeZone").GetString());
        Assert.True(detail.GetProperty("windows").GetArrayLength() > 0);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await guest.GetAsync($"/public/sites/{closedId}")).StatusCode
        );
    }

    [Fact]
    public async Task Guest_is_tenant_isolated_and_unknown_host_sees_nothing()
    {
        var (openId, _) = await SeedSites();

        // org B's guest surface never shows org A's sites, by id or list
        var otherGuest = GuestFor("org-b");
        var otherSites = await otherGuest.GetFromJsonAsync<JsonElement>("/public/sites");
        Assert.DoesNotContain(
            otherSites.EnumerateArray(),
            s => s.GetProperty("name").GetString() == "Public Open Store"
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await otherGuest.GetAsync($"/public/sites/{openId}")).StatusCode
        );

        // unknown host: empty, never an error (fail closed, gracefully)
        var lost = GuestFor("nobody-here");
        var nothing = await lost.GetFromJsonAsync<JsonElement>("/public/sites");
        Assert.Equal(0, nothing.GetArrayLength());
    }

    [Fact]
    public async Task Guest_holds_only_public_read()
    {
        await SeedSites();
        var guest = GuestFor("org-a");
        // the management surface stays dark for guests
        var management = await ApiFixture.GetItemsAsync(guest, "/api/sites");
        Assert.Equal(0, management.GetArrayLength());
    }

    [Fact]
    public async Task Org_identity_serves_the_page_shell_and_vanishes_for_unknown_hosts()
    {
        // known host: name + slug + the seeded brand color (its first reader)
        var guest = GuestFor("org-a");
        var identity = await guest.GetFromJsonAsync<JsonElement>("/public/org");
        Assert.Equal("Org A", identity.GetProperty("name").GetString());
        Assert.Equal("org-a", identity.GetProperty("slug").GetString());
        Assert.Equal("#B01458", identity.GetProperty("brandColor").GetString());

        // unknown host: 404, the page renders unbranded - never an error page
        var stranger = GuestFor("nobody");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync("/public/org")).StatusCode);
    }
}
