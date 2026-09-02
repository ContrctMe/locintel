using LocIntel.Modules.Cases.Cases;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Cases.Data;

public sealed class CasesDbContext(DbContextOptions<CasesDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "cases";

    public DbSet<Case> Cases => Set<Case>();
    public DbSet<CaseIncident> Incidents => Set<CaseIncident>();
    public DbSet<CaseEntity> Entities => Set<CaseEntity>();
    public DbSet<CaseMember> Members => Set<CaseMember>();
    public DbSet<CaseTask> Tasks => Set<CaseTask>();
    public DbSet<CaseNote> Notes => Set<CaseNote>();
    public DbSet<CaseEvidence> Evidence => Set<CaseEvidence>();
    public DbSet<CustodyEvent> Custody => Set<CustodyEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("ltree");

        modelBuilder.Entity<Case>(b =>
        {
            b.ToTable("cases");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
            b.Property(x => x.Summary).HasColumnName("summary");
            b.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.Priority)
                .HasColumnName("priority")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.LeadId).HasColumnName("lead_id");
            b.Property(x => x.Disposition)
                .HasColumnName("disposition")
                .HasConversion<string>()
                .HasMaxLength(30);
            b.Property(x => x.ClosedAt).HasColumnName("closed_at");
            b.Property(x => x.ClosedBy).HasColumnName("closed_by");
            b.Property(x => x.ClosureNote).HasColumnName("closure_note").HasMaxLength(2000);
            b.Property(x => x.LegalHold).HasColumnName("legal_hold");
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            b.HasIndex(x => new
            {
                x.OrgId,
                x.Status,
                x.UpdatedAt,
            });
        });

        modelBuilder.Entity<CaseIncident>(b =>
        {
            b.ToTable("case_incidents");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.IncidentId).HasColumnName("incident_id");
            b.Property(x => x.SiteId).HasColumnName("site_id");
            b.Property(x => x.Path).HasColumnName("path");
            b.Property(x => x.AddedBy).HasColumnName("added_by");
            b.Property(x => x.AddedAt).HasColumnName("added_at");
            b.HasIndex(x => new { x.CaseId, x.IncidentId }).IsUnique();
            b.HasIndex(x => new { x.OrgId, x.IncidentId });
            b.HasIndex(x => x.Path).HasMethod("gist");
        });

        modelBuilder.Entity<CaseEntity>(b =>
        {
            b.ToTable("case_entities");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.EntityId).HasColumnName("entity_id");
            b.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
            b.Property(x => x.AddedBy).HasColumnName("added_by");
            b.Property(x => x.AddedAt).HasColumnName("added_at");
            b.HasIndex(x => new { x.CaseId, x.EntityId }).IsUnique();
            b.HasIndex(x => new { x.OrgId, x.EntityId });
        });

        modelBuilder.Entity<CaseMember>(b =>
        {
            b.ToTable("case_members");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.AddedBy).HasColumnName("added_by");
            b.Property(x => x.AddedAt).HasColumnName("added_at");
            b.HasIndex(x => new { x.CaseId, x.UserId }).IsUnique();
            b.HasIndex(x => new { x.OrgId, x.UserId });
        });

        modelBuilder.Entity<CaseTask>(b =>
        {
            b.ToTable("case_tasks");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
            b.Property(x => x.AssigneeId).HasColumnName("assignee_id");
            b.Property(x => x.DueAt).HasColumnName("due_at");
            b.Property(x => x.DoneAt).HasColumnName("done_at");
            b.Property(x => x.DoneBy).HasColumnName("done_by");
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            b.HasIndex(x => new { x.OrgId, x.CaseId });
        });

        modelBuilder.Entity<CaseNote>(b =>
        {
            b.ToTable("case_notes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.AuthorId).HasColumnName("author_id");
            b.Property(x => x.Body).HasColumnName("body").HasMaxLength(4000);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            b.HasIndex(x => new { x.OrgId, x.CaseId });
        });

        modelBuilder.Entity<CaseEvidence>(b =>
        {
            b.ToTable("case_evidence");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.FileId).HasColumnName("file_id");
            b.Property(x => x.Label).HasColumnName("label").HasMaxLength(200);
            b.Property(x => x.AddedBy).HasColumnName("added_by");
            b.Property(x => x.AddedAt).HasColumnName("added_at");
            b.HasIndex(x => new { x.CaseId, x.FileId }).IsUnique();
            b.HasIndex(x => new { x.OrgId, x.FileId });
        });

        modelBuilder.Entity<CustodyEvent>(b =>
        {
            b.ToTable("custody_events");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.FileId).HasColumnName("file_id");
            b.Property(x => x.Action)
                .HasColumnName("action")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.ActorId).HasColumnName("actor_id");
            b.Property(x => x.ActorTier).HasColumnName("actor_tier").HasMaxLength(20);
            b.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(500);
            b.Property(x => x.At).HasColumnName("at");
            b.HasIndex(x => new
            {
                x.OrgId,
                x.CaseId,
                x.At,
            });
        });
    }
}
