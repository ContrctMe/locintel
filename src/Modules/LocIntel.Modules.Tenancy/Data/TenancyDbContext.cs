using LocIntel.Modules.Tenancy.Hierarchy;
using LocIntel.Modules.Tenancy.Organizations;
using LocIntel.Modules.Tenancy.Sites;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Spatial;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NetTopologySuite.Geometries;

namespace LocIntel.Modules.Tenancy.Data;

[SpatialModule] // sites.location is geography (ADR 50)
public sealed class TenancyDbContext(
    DbContextOptions<TenancyDbContext> options,
    ITenantContext tenant
) : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "tenancy";

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationSetting> OrganizationSettings => Set<OrganizationSetting>();
    public DbSet<OrgHierarchy> Hierarchies => Set<OrgHierarchy>();
    public DbSet<HierarchyNode> HierarchyNodes => Set<HierarchyNode>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<SiteSchedule> SiteSchedules => Set<SiteSchedule>();
    public DbSet<SiteOpenWindow> SiteOpenWindows => Set<SiteOpenWindow>();
    public DbSet<Sites.SiteAttributeDefinition> SiteAttributeDefinitions =>
        Set<Sites.SiteAttributeDefinition>();
    public DbSet<SiteSearchTerm> SiteSearchTerms => Set<SiteSearchTerm>();

    /// <summary>
    /// sites.location is derived, never set by hand (ADR 50): every save
    /// rewrites it from Latitude/Longitude, so ingest, the API, and the dev
    /// seed all keep the geography in step without knowing it exists.
    /// </summary>
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var entry in ChangeTracker.Entries<Site>().ToList())
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                SyncLocation(entry);
                SyncKeys(entry);
                await SyncSearchTermsAsync(entry, cancellationToken);
            }
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// The leakproof keys (ADR 51) follow their sources the same way the
    /// geography does: cell from the coordinates, path_text from the path.
    /// </summary>
    private static void SyncKeys(EntityEntry<Site> entry)
    {
        var site = entry.Entity;
        var cell = SpatialCells.Key(site.Latitude, site.Longitude);
        if (site.Cell != cell)
            site.Cell = cell;
        var pathText = site.Path.ToString();
        if (site.PathText != pathText)
            site.PathText = pathText;
    }

    /// <summary>
    /// The word index behind search (ADR 51): one row per distinct word of
    /// the name and city, lower-cased, replaced whenever either changes.
    /// </summary>
    private async Task SyncSearchTermsAsync(EntityEntry<Site> entry, CancellationToken ct)
    {
        var site = entry.Entity;
        var changed =
            entry.State == EntityState.Added
            || entry.Property(s => s.Name).IsModified
            || entry.Property(s => s.City).IsModified;
        if (!changed)
            return;
        if (entry.State == EntityState.Modified)
            await SiteSearchTerms.Where(t => t.SiteId == site.Id).ExecuteDeleteAsync(ct);
        foreach (var term in SiteSearchTerm.TermsOf(site.Name, site.City))
            SiteSearchTerms.Add(
                new SiteSearchTerm
                {
                    OrgId = site.OrgId,
                    SiteId = site.Id,
                    Term = term,
                    Name = site.Name,
                }
            );
    }

    private static void SyncLocation(EntityEntry<Site> entry)
    {
        var site = entry.Entity;
        var expected = site is { Latitude: { } lat, Longitude: { } lng }
            ? new Point(lng, lat) { SRID = 4326 } // x = longitude, y = latitude
            : null;
        var current = site.Location;
        if (expected is null && current is null)
            return;
        if (expected is not null && current is not null && expected.EqualsExact(current))
            return;
        site.Location = expected;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("ltree");
        // not a trusted extension: the migrate role creates it as owner (ADR 50)
        modelBuilder.HasPostgresExtension("postgis");

        modelBuilder.Entity<Organization>(b =>
        {
            b.ToTable("organizations");
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(o => o.Name).HasColumnName("name").HasMaxLength(200);
            b.Property(o => o.Slug).HasColumnName("slug").HasMaxLength(80);
            b.Property(o => o.Region).HasColumnName("region").HasMaxLength(40);
            b.Property(o => o.ExternalId).HasColumnName("external_id").HasMaxLength(120);
            b.Property(o => o.IsPlatform).HasColumnName("is_platform");
            b.Property(o => o.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(o => o.CloseRequestedAt).HasColumnName("close_requested_at");
            b.Property(o => o.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
            b.Property(o => o.CreatedAt).HasColumnName("created_at");
            b.HasIndex(o => o.Slug).IsUnique();
            b.HasIndex(o => o.ExternalId).IsUnique().HasFilter("external_id IS NOT NULL");
        });

        modelBuilder.Entity<OrganizationSetting>(b =>
        {
            b.ToTable("organization_settings");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(s => s.OrgId).HasColumnName("org_id");
            b.Property(s => s.Key).HasColumnName("key").HasMaxLength(120);
            b.Property(s => s.Value).HasColumnName("value");
            b.Property(s => s.DeletedAt).HasColumnName("deleted_at");
            b.HasIndex(s => new { s.OrgId, s.Key }).IsUnique().HasFilter("deleted_at IS NULL");
        });

        modelBuilder.Entity<OrgHierarchy>(b =>
        {
            b.ToTable("hierarchies");
            b.HasKey(h => h.Id);
            b.Property(h => h.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(h => h.OrgId).HasColumnName("org_id");
            b.Property(h => h.Name).HasColumnName("name").HasMaxLength(200);
            b.Property(h => h.Levels).HasColumnName("levels");
            b.Property(h => h.IsAuthoritative).HasColumnName("is_authoritative");
            // one authoritative tree per org in v1 (ADR 4)
            b.HasIndex(h => h.OrgId).IsUnique().HasFilter("is_authoritative");
        });

        modelBuilder.Entity<HierarchyNode>(b =>
        {
            b.ToTable("hierarchy_nodes");
            b.HasKey(n => n.Id);
            b.Property(n => n.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(n => n.OrgId).HasColumnName("org_id");
            b.Property(n => n.HierarchyId).HasColumnName("hierarchy_id");
            b.Property(n => n.ParentId).HasColumnName("parent_id");
            b.Property(n => n.Name).HasColumnName("name").HasMaxLength(200);
            b.Property(n => n.Depth).HasColumnName("depth");
            b.Property(n => n.Path).HasColumnName("path");
            b.HasIndex(n => n.Path).HasMethod("gist");
            b.HasIndex(n => n.HierarchyId);
        });

        modelBuilder.Entity<Site>(b =>
        {
            b.ToTable("sites");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(s => s.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
            b.Property(s => s.AttributesJson).HasColumnName("attributes").HasColumnType("jsonb");
            b.Property(s => s.OrgId).HasColumnName("org_id");
            b.Property(s => s.NodeId).HasColumnName("node_id");
            b.Property(s => s.Name).HasColumnName("name").HasMaxLength(200);
            b.Property(s => s.TimeZone).HasColumnName("time_zone").HasMaxLength(64);
            b.Property(s => s.ExternalId).HasColumnName("external_id").HasMaxLength(120);
            b.Property(s => s.Path).HasColumnName("path");
            b.Property(s => s.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(24);
            b.Property(s => s.AddressLine1).HasColumnName("address_line1").HasMaxLength(300);
            b.Property(s => s.City).HasColumnName("city").HasMaxLength(120);
            b.Property(s => s.PostalCode).HasColumnName("postal_code").HasMaxLength(20);
            b.Property(s => s.CountryCode).HasColumnName("country_code").HasMaxLength(2);
            b.Property(s => s.Latitude).HasColumnName("latitude");
            b.Property(s => s.Longitude).HasColumnName("longitude");
            // geography, never geometry, for stored data (ADR 50): meters and
            // planet-correct boxes without a projection decision per query
            b.Property(s => s.Location)
                .HasColumnName("location")
                .HasColumnType("geography (point, 4326)");
            b.HasIndex(s => s.Location).HasMethod("gist");
            b.Property(s => s.CreatedAt).HasColumnName("created_at");
            b.HasIndex(s => s.Path).HasMethod("gist");
            b.HasIndex(s => s.NodeId);
            // the leakproof keys (ADR 51): what the app role's plans actually
            // use under row security - a tile or viewport is a cell range, a
            // subtree a path_text range, a page a (name, id) keyset
            b.Property(s => s.Cell).HasColumnName("cell");
            b.Property(s => s.PathText).HasColumnName("path_text").UseCollation("C");
            b.HasIndex(s => new { s.OrgId, s.Cell });
            b.HasIndex(s => new { s.OrgId, s.PathText });
            b.HasIndex(s => new
            {
                s.OrgId,
                s.Name,
                s.Id,
            });
            b.HasIndex(s => new { s.OrgId, s.Status });
            b.HasIndex(s => new { s.OrgId, s.ExternalId })
                .IsUnique()
                .HasFilter("external_id IS NOT NULL");
        });

        modelBuilder.Entity<SiteSearchTerm>(b =>
        {
            b.ToTable("site_search_terms");
            b.HasKey(t => new { t.SiteId, t.Term });
            b.Property(t => t.OrgId).HasColumnName("org_id");
            b.Property(t => t.SiteId).HasColumnName("site_id");
            b.Property(t => t.Term).HasColumnName("term").HasMaxLength(120).UseCollation("C");
            b.Property(t => t.Name).HasColumnName("name").HasMaxLength(200);
            // the search page IS this index: term range, then name order
            b.HasIndex(t => new
            {
                t.OrgId,
                t.Term,
                t.Name,
                t.SiteId,
            });
            b.HasOne<Site>()
                .WithMany()
                .HasForeignKey(t => t.SiteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Sites.SiteAttributeDefinition>(b =>
        {
            b.ToTable("site_attribute_definitions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.Key).HasColumnName("key").HasMaxLength(60);
            b.Property(x => x.Label).HasColumnName("label").HasMaxLength(100);
            b.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Public).HasColumnName("public");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => new { x.OrgId, x.Key }).IsUnique();
        });

        modelBuilder.Entity<SiteSchedule>(b =>
        {
            b.ToTable("site_schedules");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(s => s.OrgId).HasColumnName("org_id");
            b.Property(s => s.SiteId).HasColumnName("site_id");
            b.Property(s => s.Name).HasColumnName("name").HasMaxLength(200);
            b.Property(s => s.RRule).HasColumnName("rrule").HasMaxLength(500);
            b.Property(s => s.AnchorDate).HasColumnName("anchor_date");
            b.Property(s => s.OpensLocal).HasColumnName("opens_local");
            b.Property(s => s.ClosesLocal).HasColumnName("closes_local");
            b.Property(s => s.ExDates).HasColumnName("ex_dates");
            b.HasIndex(s => s.SiteId);
        });

        modelBuilder.Entity<SiteOpenWindow>(b =>
        {
            b.ToTable("site_open_windows");
            b.HasKey(w => w.Id);
            b.Property(w => w.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(w => w.OrgId).HasColumnName("org_id");
            b.Property(w => w.SiteId).HasColumnName("site_id");
            b.Property(w => w.ScheduleId).HasColumnName("schedule_id");
            b.Property(w => w.StartsAtUtc).HasColumnName("starts_at_utc");
            b.Property(w => w.EndsAtUtc).HasColumnName("ends_at_utc");
            b.Property(w => w.LocalDate).HasColumnName("local_date");
            b.HasIndex(w => w.SiteId);
            // the open-now range query (ADR 28)
            b.HasIndex(w => new
            {
                w.OrgId,
                w.StartsAtUtc,
                w.EndsAtUtc,
            });
        });
    }
}
