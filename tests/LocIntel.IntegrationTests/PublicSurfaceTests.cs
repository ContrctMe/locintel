using System.Net;
using System.Net.Http.Json;

namespace LocIntel.IntegrationTests;

/// <summary>Cacheability + OpenAPI exposure on the normal fixture (the hardening class runs a tiny rate limit).</summary>
public class PublicSurfaceTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Public_reads_are_cacheable_and_private_reads_are_not()
    {
        var guest = fixture.GuestClient();
        guest.DefaultRequestHeaders.Host = "org-a.localhost";
        var pub = await guest.GetAsync("/public/sites");
        pub.EnsureSuccessStatusCode();
        Assert.True(pub.Headers.CacheControl?.NoStore);
        Assert.True(pub.Headers.CacheControl?.Private);

        // authenticated/private surface must never advertise itself cacheable
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var priv = await owner.GetAsync("/api/sites?limit=1");
        priv.EnsureSuccessStatusCode();
        Assert.Null(priv.Headers.CacheControl?.Public);
    }

    [Fact]
    public async Task Openapi_is_served_by_default()
    {
        // default-on so the developer page's spec link works; the off path
        // is a one-line config flip (Api:ExposeOpenApi=false), not worth a
        // second fixture
        var response = await fixture.GuestClient().GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        Assert.Contains("/api/sites", await response.Content.ReadAsStringAsync());
    }
}
