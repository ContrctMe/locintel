using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

public class SiteMetadataTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Explicit_ids_keep_tenant_and_grant_filters()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        using var other = await fixture.LoginAsync(ApiFixture.UserB);
        using var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        var own = await ApiFixture.EnsureSiteAsync(owner, "Metadata A");
        var foreign = await ApiFixture.EnsureSiteAsync(other, "Metadata B");
        var url = $"/api/sites?ids={own},{foreign},{own},{Guid.NewGuid()}&limit=200";
        var page = await owner.GetFromJsonAsync<JsonElement>(url);
        Assert.Equal(
            own,
            Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid()
        );
        var denied = await viewer.GetFromJsonAsync<JsonElement>(url);
        Assert.Empty(denied.GetProperty("items").EnumerateArray());
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await owner.GetAsync("/api/sites?ids=not-a-uuid")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await owner.GetAsync(
                    "/api/sites?ids=" + string.Join(',', Enumerable.Repeat(own, 201))
                )
            ).StatusCode
        );
    }
}
