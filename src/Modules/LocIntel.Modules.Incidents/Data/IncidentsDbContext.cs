using LocIntel.Modules.Incidents.Incidents;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Incidents.Data;

public sealed class IncidentsDbContext(
    DbContextOptions<IncidentsDbContext> options,
    ITenantContext tenant
) : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "incidents";

    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentNote> Notes => Set<IncidentNote>();
    public DbSet<IncidentAttachment> Attachments => Set<IncidentAttachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("ltree");

        modelBuilder.Entity<Incident>(b =>
        {
            b.ToTable("incidents");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.SiteId).HasColumnName("site_id");
            b.Property(x => x.HierarchyId).HasColumnName("hierarchy_id");
            b.Property(x => x.Path).HasColumnName("path");
            b.Property(x => x.Category)
                .HasColumnName("category")
                .HasConversion<string>()
                .HasMaxLength(40);
            b.Property(x => x.Severity)
                .HasColumnName("severity")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.Source)
                .HasColumnName("source")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
            b.Property(x => x.Narrative).HasColumnName("narrative");
            b.Property(x => x.LocationDetail).HasColumnName("location_detail").HasMaxLength(200);
            b.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            b.Property(x => x.BusinessDate).HasColumnName("business_date");
            b.Property(x => x.ReportedAt).HasColumnName("reported_at");
            b.Property(x => x.ReportedBy).HasColumnName("reported_by");
            b.Property(x => x.ReporterContact).HasColumnName("reporter_contact").HasMaxLength(320);
            b.Property(x => x.LossAmount).HasColumnName("loss_amount").HasPrecision(14, 2);
            b.Property(x => x.RecoveredAmount)
                .HasColumnName("recovered_amount")
                .HasPrecision(14, 2);
            b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
            b.Property(x => x.PoliceReportNumber)
                .HasColumnName("police_report_number")
                .HasMaxLength(100);
            b.Property(x => x.Tags).HasColumnName("tags");
            b.Property(x => x.ClosedAt).HasColumnName("closed_at");
            b.Property(x => x.ClosedBy).HasColumnName("closed_by");
            b.Property(x => x.ClosureReason).HasColumnName("closure_reason").HasMaxLength(500);
            b.Property(x => x.LegalHold).HasColumnName("legal_hold");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            b.HasIndex(x => x.Path).HasMethod("gist");
            // the rollup shape: org, business date, then whatever the report groups by
            b.HasIndex(x => new
            {
                x.OrgId,
                x.BusinessDate,
                x.Category,
            });
            b.HasIndex(x => new
            {
                x.OrgId,
                x.SiteId,
                x.OccurredAt,
            });
        });

        modelBuilder.Entity<IncidentNote>(b =>
        {
            b.ToTable("notes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.IncidentId).HasColumnName("incident_id");
            b.Property(x => x.AuthorId).HasColumnName("author_id");
            b.Property(x => x.Body).HasColumnName("body").HasMaxLength(4000);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            b.HasIndex(x => new { x.OrgId, x.IncidentId });
        });

        modelBuilder.Entity<IncidentAttachment>(b =>
        {
            b.ToTable("attachments");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.IncidentId).HasColumnName("incident_id");
            b.Property(x => x.FileId).HasColumnName("file_id");
            b.Property(x => x.Label).HasColumnName("label").HasMaxLength(200);
            b.Property(x => x.AddedBy).HasColumnName("added_by");
            b.Property(x => x.AddedAt).HasColumnName("added_at");
            b.HasIndex(x => new { x.IncidentId, x.FileId }).IsUnique();
            b.HasIndex(x => new { x.OrgId, x.IncidentId });
        });
    }
}
