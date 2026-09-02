using LocIntel.Modules.Alerts.Alerts;
using LocIntel.Modules.Alerts.Bulletins;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Alerts.Data;

public sealed class AlertsDbContext(
    DbContextOptions<AlertsDbContext> options,
    ITenantContext tenant
) : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "alerts";

    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AlertRead> Reads => Set<AlertRead>();
    public DbSet<Bulletin> Bulletins => Set<Bulletin>();
    public DbSet<BulletinAcknowledgement> Acknowledgements => Set<BulletinAcknowledgement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("ltree");

        modelBuilder.Entity<Alert>(b =>
        {
            b.ToTable("alerts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.Severity)
                .HasColumnName("severity")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
            b.Property(x => x.Body).HasColumnName("body").HasMaxLength(2000);
            b.Property(x => x.Path).HasColumnName("path");
            b.Property(x => x.IncidentId).HasColumnName("incident_id");
            b.Property(x => x.BulletinId).HasColumnName("bulletin_id");
            b.Property(x => x.EntityId).HasColumnName("entity_id");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => new { x.OrgId, x.CreatedAt });
            b.HasIndex(x => x.Path).HasMethod("gist");
        });

        modelBuilder.Entity<AlertRead>(b =>
        {
            b.ToTable("alert_reads");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.AlertId).HasColumnName("alert_id");
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.ReadAt).HasColumnName("read_at");
            b.HasIndex(x => new { x.AlertId, x.UserId }).IsUnique();
            b.HasIndex(x => new { x.OrgId, x.UserId });
        });

        modelBuilder.Entity<Bulletin>(b =>
        {
            b.ToTable("bulletins");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Severity)
                .HasColumnName("severity")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
            b.Property(x => x.Body).HasColumnName("body");
            b.Property(x => x.ScopePath).HasColumnName("scope_path");
            b.Property(x => x.EntityId).HasColumnName("entity_id");
            b.Property(x => x.IncidentId).HasColumnName("incident_id");
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.IssuedBy).HasColumnName("issued_by");
            b.Property(x => x.IssuedAt).HasColumnName("issued_at");
            b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            b.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.WithdrawnAt).HasColumnName("withdrawn_at");
            b.Property(x => x.WithdrawnBy).HasColumnName("withdrawn_by");
            b.HasIndex(x => new
            {
                x.OrgId,
                x.Status,
                x.ExpiresAt,
            });
            b.HasIndex(x => x.ScopePath).HasMethod("gist");
        });

        modelBuilder.Entity<BulletinAcknowledgement>(b =>
        {
            b.ToTable("bulletin_acknowledgements");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.BulletinId).HasColumnName("bulletin_id");
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.SiteId).HasColumnName("site_id");
            b.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
            b.Property(x => x.AcknowledgedAt).HasColumnName("acknowledged_at");
            b.HasIndex(x => new { x.BulletinId, x.UserId }).IsUnique();
        });
    }
}
