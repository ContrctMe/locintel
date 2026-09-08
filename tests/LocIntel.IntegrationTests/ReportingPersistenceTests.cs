using LocIntel.Contracts;
using LocIntel.Modules.Reporting;
using LocIntel.Modules.Reporting.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LocIntel.IntegrationTests;

public sealed class ReportingPersistenceTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Quota_is_tenant_isolated_and_settlement_keeps_the_reserved_month()
    {
        using var a = Scope(fixture.OrgA);
        var db = a.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var quota = a.ServiceProvider.GetRequiredService<LocIntel.Modules.Reporting.ReportQuota>();
        var month = quota.PeriodMonth;
        var previous = month.AddMonths(-1);
        var entry = new ReportQuotaEntry
        {
            Id = Guid.CreateVersion7(),
            OrgId = fixture.OrgA,
            JobId = Guid.CreateVersion7(),
            PeriodMonth = previous,
        };
        db.QuotaEntries.Add(entry);
        await db.SaveChangesAsync();
        var before = await quota.CurrentUsageAsync(fixture.OrgA);
        using (var b = Scope(fixture.OrgB))
        {
            var other = b.ServiceProvider.GetRequiredService<ReportingDbContext>();
            Assert.False(
                await other.QuotaEntries.IgnoreQueryFilters().AnyAsync(x => x.Id == entry.Id)
            );
            Assert.Equal(
                0,
                await other
                    .QuotaEntries.IgnoreQueryFilters()
                    .Where(x => x.Id == entry.Id)
                    .ExecuteDeleteAsync()
            );
            other.QuotaEntries.Add(
                new ReportQuotaEntry
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = fixture.OrgA,
                    JobId = entry.JobId,
                    PeriodMonth = month,
                }
            );
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
            Assert.Equal("42501", Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await LocIntel.Modules.Reporting.ReportQuota.SettleAsync(
                db,
                fixture.OrgA,
                entry.Id,
                true,
                default
            );
            await tx.CommitAsync();
        }
        var settled = await db.QuotaEntries.AsNoTracking().SingleAsync(x => x.Id == entry.Id);
        Assert.True(settled.Consumed);
        Assert.Equal(previous, settled.PeriodMonth);
        Assert.Equal(before, await quota.CurrentUsageAsync(fixture.OrgA));
    }

    [Fact]
    public async Task Overlay_source_preserves_multipolygons_and_holes_and_enforces_scope()
    {
        using var scope = Scope(fixture.OrgA);
        var db =
            scope.ServiceProvider.GetRequiredService<LocIntel.Modules.Spatial.Data.SpatialDbContext>();
        var path = new LTree("reporting_fixture");
        var layer = new LocIntel.Modules.Spatial.Overlays.OverlayLayer
        {
            Id = Guid.CreateVersion7(),
            OrgId = fixture.OrgA,
            Name = "Report overlay",
            Kind = "territory",
            NodeId = Guid.NewGuid(),
            HierarchyId = Guid.NewGuid(),
            Path = path,
            CreatedBy = Guid.NewGuid(),
        };
        var geometry = new NetTopologySuite.IO.WKTReader().Read(
            "MULTIPOLYGON (((0 0, 4 0, 4 4, 0 4, 0 0), (1 1, 1 2, 2 2, 2 1, 1 1)), ((10 10, 11 10, 11 11, 10 11, 10 10)))"
        );
        geometry.SRID = 4326;
        db.Layers.Add(layer);
        db.Features.Add(
            new LocIntel.Modules.Spatial.Overlays.OverlayFeature
            {
                Id = Guid.CreateVersion7(),
                OrgId = fixture.OrgA,
                LayerId = layer.Id,
                Geom = geometry,
                HierarchyId = layer.HierarchyId,
                Path = path,
            }
        );
        await db.SaveChangesAsync();
        var source = scope.ServiceProvider.GetRequiredService<IReportOverlaySource>();
        var result = Assert.Single(
            await source.ReadAsync(fixture.OrgA, new NodeScope.EntireOrg(fixture.OrgA), [layer.Id])
        );
        Assert.Equal(2, result.Polygons.Length);
        Assert.Equal(2, result.Polygons[0].Length);
        Assert.Equal(new[] { 1d, 1d }, result.Polygons[0][1][0]);
        Assert.Empty(
            await source.ReadAsync(
                fixture.OrgA,
                new NodeScope.Subtrees(fixture.OrgA, ["other"]),
                [layer.Id]
            )
        );
        layer.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        Assert.Empty(
            await source.ReadAsync(fixture.OrgA, new NodeScope.EntireOrg(fixture.OrgA), [layer.Id])
        );
    }

    [Fact]
    public async Task Site_source_reports_human_hierarchy_and_local_operating_windows_without_truncating_to_limit()
    {
        using var scope = Scope(fixture.OrgA);
        var db =
            scope.ServiceProvider.GetRequiredService<LocIntel.Modules.Tenancy.Data.TenancyDbContext>();
        var node = LocIntel.Modules.Tenancy.Hierarchy.HierarchyNode.CreateRoot(
            fixture.OrgA,
            Guid.NewGuid(),
            "Québec"
        );
        db.HierarchyNodes.Add(node);
        var ids = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var id = SiteId.New();
            ids.Add(id.Value);
            db.Sites.Add(
                new LocIntel.Modules.Tenancy.Sites.Site
                {
                    Id = id,
                    OrgId = fixture.OrgA,
                    NodeId = node.Id,
                    Name = $"Report site {i}",
                    TimeZone = "America/Toronto",
                    Path = new LTree(
                        node.Path + "." + LocIntel.Modules.Tenancy.Sites.Site.Label(id)
                    ),
                }
            );
        }
        var start = new DateTimeOffset(2026, 9, 7, 13, 0, 0, TimeSpan.Zero);
        db.SiteOpenWindows.Add(
            new LocIntel.Modules.Tenancy.Sites.SiteOpenWindow
            {
                Id = Guid.CreateVersion7(),
                OrgId = fixture.OrgA,
                SiteId = new SiteId(ids[0]),
                ScheduleId = Guid.NewGuid(),
                LocalDate = new DateOnly(2026, 9, 7),
                StartsAtUtc = start,
                EndsAtUtc = start.AddHours(8),
            }
        );
        await db.SaveChangesAsync();
        var source = scope.ServiceProvider.GetRequiredService<ISiteSource>();
        var selected = await source.SelectAsync(
            fixture.OrgA,
            new NodeScope.EntireOrg(fixture.OrgA),
            ids.ToArray(),
            1
        );
        Assert.Equal(2, selected.Count); // overflow is detectable by admission, never silently truncated
        Assert.All(selected, site => Assert.Equal("Québec", site.Hierarchy));
        var hours = Assert.Single(
            await source.HoursAsync(
                fixture.OrgA,
                new NodeScope.EntireOrg(fixture.OrgA),
                ids.ToArray(),
                new(2026, 9, 7),
                new(2026, 9, 8)
            )
        );
        Assert.Equal(start, hours.StartsAtUtc);
        Assert.Empty(
            await source.HoursAsync(
                fixture.OrgA,
                NodeScope.Nothing,
                ids.ToArray(),
                new(2026, 9, 7),
                new(2026, 9, 8)
            )
        );
    }

    [Fact]
    public async Task Background_requester_requires_current_membership_and_an_active_organization()
    {
        using var scope = Scope(fixture.OrgA);
        var db =
            scope.ServiceProvider.GetRequiredService<LocIntel.Modules.Identity.Data.IdentityDbContext>();
        var source = scope.ServiceProvider.GetRequiredService<IReportRequester>();
        var user = await db.Users.SingleAsync(x => x.Email == ApiFixture.UserA);
        var current = await source.GetActiveAsync(fixture.OrgA, user.Id);
        Assert.NotNull(current);
        Assert.False(current.Impersonating);
        Assert.Null(await source.GetActiveAsync(fixture.OrgB, user.Id));
        await using var tx = await db.Database.BeginTransactionAsync();
        var directory = await db.OrgDirectory.SingleAsync(x => x.OrgId == fixture.OrgA);
        directory.Status = "Suspended";
        await db.SaveChangesAsync();
        Assert.Null(await source.GetActiveAsync(fixture.OrgA, user.Id));
        directory.Status = "Active";
        await db.SaveChangesAsync();
        await db
            .Memberships.Where(x => x.OrgId == fixture.OrgA && x.UserId == user.Id)
            .ExecuteDeleteAsync();
        Assert.Null(await source.GetActiveAsync(fixture.OrgA, user.Id));
        await tx.RollbackAsync();
    }

    private IServiceScope Scope(OrgId org)
    {
        var scope = fixture.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(org, RegionId.Default);
        return scope;
    }

    private static ReportJob Job(OrgId org) =>
        new()
        {
            OrgId = org,
            RequestedBy = Guid.NewGuid(),
            ReportType = "site",
            DefinitionVersion = 1,
            Mode = ReportMode.Single,
            Selection = ReportSelection.Selected,
            SiteIds = [Guid.NewGuid()],
            OptionsJson = "{}",
        };

    [Fact]
    public async Task Report_rows_remain_private_even_without_EF_filters_and_cascade_on_cleanup()
    {
        var job = Job(fixture.OrgA);
        var item = new ReportItem
        {
            OrgId = fixture.OrgA,
            JobId = job.Id,
            SiteIds = job.SiteIds,
        };
        var artifact = new ReportArtifact
        {
            OrgId = fixture.OrgA,
            JobId = job.Id,
            ItemId = item.Id,
            Revision = 0,
            Key = $"reports/{fixture.OrgA.Value}/{Guid.NewGuid()}",
            Name = "site.pdf",
            ContentType = "application/pdf",
        };
        using var a = Scope(fixture.OrgA);
        var db = a.ServiceProvider.GetRequiredService<ReportingDbContext>();
        db.Jobs.Add(job);
        db.Items.Add(item);
        db.Artifacts.Add(artifact);
        await db.SaveChangesAsync();
        using (var b = Scope(fixture.OrgB))
        {
            var other = b.ServiceProvider.GetRequiredService<ReportingDbContext>();
            Assert.False(await other.Jobs.IgnoreQueryFilters().AnyAsync(x => x.Id == job.Id));
            Assert.False(await other.Items.IgnoreQueryFilters().AnyAsync(x => x.Id == item.Id));
            Assert.False(
                await other.Artifacts.IgnoreQueryFilters().AnyAsync(x => x.Id == artifact.Id)
            );
            other.Jobs.Add(Job(fixture.OrgA));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
            Assert.Equal("42501", Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        await db.Jobs.Where(x => x.Id == job.Id).ExecuteDeleteAsync();
        Assert.False(await db.Items.AnyAsync(x => x.Id == item.Id));
        Assert.False(await db.Artifacts.AnyAsync(x => x.Id == artifact.Id));
    }

    [Fact]
    public async Task A_child_cannot_reference_another_organizations_job()
    {
        using var a = Scope(fixture.OrgA);
        var db = a.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var job = Job(fixture.OrgA);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        using var b = Scope(fixture.OrgB);
        var other = b.ServiceProvider.GetRequiredService<ReportingDbContext>();
        other.Items.Add(
            new ReportItem
            {
                OrgId = fixture.OrgB,
                JobId = job.Id,
                SiteIds = [],
            }
        );
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
        Assert.Equal("23503", Assert.IsType<PostgresException>(error.InnerException).SqlState);
        await db.Jobs.Where(x => x.Id == job.Id).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Report_sources_respect_empty_scope_and_refuse_unbounded_date_ranges()
    {
        using var a = Scope(fixture.OrgA);
        var sites = a.ServiceProvider.GetRequiredService<ISiteSource>();
        Assert.Empty(await sites.SelectAsync(fixture.OrgA, NodeScope.Nothing, null, 100));
        Assert.Empty(
            await sites.SelectAsync(fixture.OrgB, new NodeScope.EntireOrg(fixture.OrgB), null, 100)
        );
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            sites.HoursAsync(
                fixture.OrgA,
                new NodeScope.EntireOrg(fixture.OrgA),
                [],
                new DateOnly(2026, 1, 1),
                new DateOnly(2027, 1, 1)
            )
        );
        Assert.Empty(
            await a
                .ServiceProvider.GetRequiredService<IReportOverlaySource>()
                .ReadAsync(fixture.OrgA, NodeScope.Nothing, [Guid.NewGuid()])
        );
    }
}
