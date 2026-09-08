using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.IntegrationTests;

public class TenantFilterBindingTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public void Cached_model_binds_the_current_tenant_without_relying_on_RLS()
    {
        var options = new DbContextOptionsBuilder<TenancyDbContext>()
            .UseNpgsql(fixture.AppConnectionString, options => options.UseNetTopologySuite())
            .Options;
        var tenantA = new TenantContext();
        tenantA.Set(fixture.OrgA, RegionId.Default);
        var tenantB = new TenantContext();
        tenantB.Set(fixture.OrgB, RegionId.Default);
        using var a = new TenancyDbContext(options, tenantA);
        using var b = new TenancyDbContext(options, tenantB);
        using var empty = new TenancyDbContext(options, new TenantContext());
        Assert.Same(a.Model, b.Model);
        Assert.Same(a.Model, empty.Model);

        // Inspect EF's parameters before PostgreSQL's independent RLS guard.
        var sqlA = a.OrganizationSettings.ToQueryString();
        var sqlB = b.OrganizationSettings.ToQueryString();
        var sqlEmpty = empty.OrganizationSettings.ToQueryString();
        Assert.Contains(fixture.OrgA.Value.ToString(), sqlA);
        Assert.DoesNotContain(fixture.OrgB.Value.ToString(), sqlA);
        Assert.Contains(fixture.OrgB.Value.ToString(), sqlB);
        Assert.DoesNotContain(fixture.OrgA.Value.ToString(), sqlB);
        Assert.Contains(Guid.Empty.ToString(), sqlEmpty);
        Assert.DoesNotContain(fixture.OrgA.Value.ToString(), sqlEmpty);
        Assert.DoesNotContain(fixture.OrgB.Value.ToString(), sqlEmpty);
    }
}
