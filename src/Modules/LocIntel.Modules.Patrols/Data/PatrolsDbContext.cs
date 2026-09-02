using LocIntel.Modules.Patrols.Patrols;
using LocIntel.Modules.Patrols.Routes;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Patrols.Data;

public sealed class PatrolsDbContext(
    DbContextOptions<PatrolsDbContext> options,
    ITenantContext tenant
) : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "patrols";

    public DbSet<PatrolRoute> Routes => Set<PatrolRoute>();
    public DbSet<PatrolSchedule> Schedules => Set<PatrolSchedule>();
    public DbSet<Patrol> Patrols => Set<Patrol>();
    public DbSet<PatrolScan> Scans => Set<PatrolScan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("ltree");

        modelBuilder.Entity<PatrolRoute>(b =>
        {
            b.ToTable("routes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.SiteId).HasColumnName("site_id");
            b.Property(x => x.Path).HasColumnName("path");
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
            b.Property(x => x.CheckpointsJson).HasColumnName("checkpoints").HasColumnType("jsonb");
            b.Property(x => x.ExpectedMinutes).HasColumnName("expected_minutes");
            b.Property(x => x.Archived).HasColumnName("archived");
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.HasIndex(x => new { x.OrgId, x.SiteId });
            b.HasIndex(x => x.Path).HasMethod("gist");
        });

        modelBuilder.Entity<PatrolSchedule>(b =>
        {
            b.ToTable("schedules");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.RouteId).HasColumnName("route_id");
            b.Property(x => x.SiteId).HasColumnName("site_id");
            b.Property(x => x.RRule).HasColumnName("rrule").HasMaxLength(500);
            b.Property(x => x.AnchorDate).HasColumnName("anchor_date");
            b.Property(x => x.StartLocal).HasColumnName("start_local");
            b.Property(x => x.ExDates).HasColumnName("ex_dates");
            b.Property(x => x.Active).HasColumnName("active");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => new { x.OrgId, x.SiteId });
        });

        modelBuilder.Entity<Patrol>(b =>
        {
            b.ToTable("patrols");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.RouteId).HasColumnName("route_id");
            b.Property(x => x.SiteId).HasColumnName("site_id");
            b.Property(x => x.Path).HasColumnName("path");
            b.Property(x => x.BusinessDate).HasColumnName("business_date");
            b.Property(x => x.ScheduledStartLocal).HasColumnName("scheduled_start_local");
            b.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.StartedAt).HasColumnName("started_at");
            b.Property(x => x.StartedBy).HasColumnName("started_by");
            b.Property(x => x.StartedByTier).HasColumnName("started_by_tier").HasMaxLength(20);
            b.Property(x => x.EndedAt).HasColumnName("ended_at");
            b.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(4000);
            b.HasIndex(x => new
            {
                x.OrgId,
                x.SiteId,
                x.BusinessDate,
            });
            b.HasIndex(x => x.Path).HasMethod("gist");
        });

        modelBuilder.Entity<PatrolScan>(b =>
        {
            b.ToTable("scans");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.PatrolId).HasColumnName("patrol_id");
            b.Property(x => x.Code).HasColumnName("code").HasMaxLength(40);
            b.Property(x => x.ScannedAt).HasColumnName("scanned_at");
            b.Property(x => x.Latitude).HasColumnName("latitude");
            b.Property(x => x.Longitude).HasColumnName("longitude");
            b.Property(x => x.DistanceMeters).HasColumnName("distance_m");
            b.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
            b.HasIndex(x => new { x.PatrolId, x.ScannedAt });
        });
    }
}
