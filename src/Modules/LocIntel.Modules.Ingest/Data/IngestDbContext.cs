using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Ingest.Data;

public sealed class IngestDbContext(
    DbContextOptions<IngestDbContext> options,
    ITenantContext tenant
) : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "ingest";

    public DbSet<ImportBatch> Batches => Set<ImportBatch>();
    public DbSet<StagedSite> StagedSites => Set<StagedSite>();
    public DbSet<SiteConnector> Connectors => Set<SiteConnector>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ImportBatch>(b =>
        {
            b.ToTable("import_batches");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.Source).HasColumnName("source").HasMaxLength(120);
            b.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(16);
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.Counts).HasColumnName("counts").HasColumnType("jsonb");
            b.HasIndex(x => new { x.OrgId, x.CreatedAt });
        });

        modelBuilder.Entity<StagedSite>(b =>
        {
            b.ToTable("staged_sites");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.BatchId).HasColumnName("batch_id");
            b.Property(x => x.ExternalId).HasColumnName("external_id").HasMaxLength(120);
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
            b.Property(x => x.TimeZone).HasColumnName("time_zone").HasMaxLength(64);
            b.Property(x => x.NodePath).HasColumnName("node_path").HasMaxLength(500);
            b.Property(x => x.NodeId).HasColumnName("node_id");
            b.Property(x => x.SourceStatus).HasColumnName("source_status").HasMaxLength(10);
            b.Property(x => x.Action).HasColumnName("action").HasMaxLength(10);
            b.Property(x => x.Errors).HasColumnName("errors");
            b.Property(x => x.Changes).HasColumnName("changes");
            b.HasIndex(x => x.BatchId);
        });

        modelBuilder.Entity<SiteConnector>(b =>
        {
            b.ToTable("site_connectors");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(120);
            b.Property(x => x.Type).HasColumnName("type").HasMaxLength(40);
            b.Property(x => x.Url).HasColumnName("url").HasMaxLength(1000);
            b.Property(x => x.EncryptedCredentials).HasColumnName("encrypted_credentials");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.LastSyncedAt).HasColumnName("last_synced_at");
            b.Property(x => x.SyncIntervalHours).HasColumnName("sync_interval_hours");
            b.HasIndex(x => new { x.OrgId, x.Name }).IsUnique();
        });
    }
}
