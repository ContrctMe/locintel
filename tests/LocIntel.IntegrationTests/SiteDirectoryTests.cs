using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

/// <summary>
/// SiteInfo is a published contract other modules build on, so its fields
/// must actually arrive populated - a record that compiles with everything
/// null passes a build and fails a consumer. A fork extended this contract
/// three times (hierarchy id, coordinates, country); these pin the shape.
/// </summary>
public class SiteDirectoryTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Site_info_carries_hierarchy_id_and_location()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var rootId = await ApiFixture.EnsureRootAsync(owner, "Org A");

        var created = await owner.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Directory Probe",
                timeZone = "America/New_York",
                city = "Boston",
                postalCode = "02110",
                countryCode = "US",
                latitude = 42.3601,
                longitude = -71.0589,
            }
        );
        created.EnsureSuccessStatusCode();
        var siteId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var tenant =
            scope.ServiceProvider.GetRequiredService<LocIntel.Platform.Kernel.TenantContext>();
        tenant.Set(fixture.OrgA, LocIntel.Platform.Kernel.RegionId.Default);
        var directory = scope.ServiceProvider.GetRequiredService<ISiteDirectory>();

        var info = await directory.FindAsync(siteId);
        Assert.NotNull(info);
        Assert.Equal("Directory Probe", info.Name);
        Assert.Equal("America/New_York", info.TimeZone);
        // the ADR 2/4 stamping key: a consumer cannot derive this from the path
        Assert.NotEqual(Guid.Empty, info.HierarchyId);
        Assert.Equal(42.3601, info.Latitude);
        Assert.Equal(-71.0589, info.Longitude);
        Assert.Equal("Boston", info.City);
        Assert.Equal("US", info.CountryCode);
    }
}
