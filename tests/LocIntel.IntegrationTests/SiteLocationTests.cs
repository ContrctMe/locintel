using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;

namespace LocIntel.IntegrationTests;

/// <summary>
/// ADR 50 §1-2 against the real PostGIS image: the extension exists, a site
/// saved through the API carries a geography derived from its doubles, a
/// spatial predicate translates and runs on that column, and clearing the
/// doubles clears the geography. The bbox endpoint (ADR 49) builds on this.
/// </summary>
public class SiteLocationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Postgis_is_installed_by_the_migrate_role()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        await TenantScope.RunAsAsync(
            scope.ServiceProvider,
            fixture.OrgA,
            async scoped =>
            {
                var db = scoped.GetRequiredService<TenancyDbContext>();
                var version = await db
                    .Database.SqlQueryRaw<string>("SELECT postgis_lib_version() AS \"Value\"")
                    .SingleAsync();
                Assert.StartsWith("3.", version);
            }
        );
    }

    [Fact]
    public async Task Location_is_derived_from_the_doubles_and_queryable()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var hawthorne = await ApiFixture.EnsureSiteAsync(
            owner,
            "Hawthorne (geo)",
            "America/Los_Angeles",
            latitude: 45.5122,
            longitude: -122.6284
        );
        var noCoords = await ApiFixture.EnsureSiteAsync(owner, "No coordinates (geo)");

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        await TenantScope.RunAsAsync(
            scope.ServiceProvider,
            fixture.OrgA,
            async scoped =>
            {
                var db = scoped.GetRequiredService<TenancyDbContext>();

                var site = await db.Sites.SingleAsync(s => s.Id == new SiteId(hawthorne));
                Assert.NotNull(site.Location);
                Assert.Equal(4326, site.Location!.SRID);
                Assert.Equal(-122.6284, site.Location.X, 6); // x = longitude
                Assert.Equal(45.5122, site.Location.Y, 6); // y = latitude

                var empty = await db.Sites.SingleAsync(s => s.Id == new SiteId(noCoords));
                Assert.Null(empty.Location);

                // a Portland-sized box: the spatial predicate runs in SQL on the
                // geography column, and a site without coordinates is never inside it
                var portland = new GeometryFactory(new PrecisionModel(), 4326).ToGeometry(
                    new Envelope(-122.8, -122.5, 45.4, 45.6)
                );
                var inside = await db
                    .Sites.Where(s => s.Location != null && s.Location.Intersects(portland))
                    .Select(s => s.Name)
                    .ToListAsync();
                Assert.Contains("Hawthorne (geo)", inside);
                Assert.DoesNotContain("No coordinates (geo)", inside);

                // clearing the doubles clears the geography on the same save
                site.Latitude = null;
                site.Longitude = null;
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                var cleared = await db.Sites.SingleAsync(s => s.Id == new SiteId(hawthorne));
                Assert.Null(cleared.Location);
            }
        );
    }
}
