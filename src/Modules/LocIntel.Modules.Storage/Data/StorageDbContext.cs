using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Storage.Data;

public sealed class StorageDbContext(
    DbContextOptions<StorageDbContext> options,
    ITenantContext tenant
) : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "storage";

    public DbSet<FileObject> Files => Set<FileObject>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<FileObject>(b =>
        {
            b.ToTable("files");
            b.HasKey(f => f.Id);
            b.Property(f => f.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(f => f.OrgId).HasColumnName("org_id");
            b.Property(f => f.Key).HasColumnName("key").HasMaxLength(500);
            b.Property(f => f.Name).HasColumnName("name").HasMaxLength(300);
            b.Property(f => f.ContentType).HasColumnName("content_type").HasMaxLength(100);
            b.Property(f => f.MaxBytes).HasColumnName("max_bytes");
            b.Property(f => f.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(f => f.LegalHold).HasColumnName("legal_hold");
            b.Property(f => f.DeletedAt).HasColumnName("deleted_at");
            b.Property(f => f.PreviewKey).HasColumnName("preview_key").HasMaxLength(520);
            b.Property(f => f.CreatedBy).HasColumnName("created_by");
            b.Property(f => f.CreatedAt).HasColumnName("created_at");
            b.Property(f => f.ScannedAt).HasColumnName("scanned_at");
            b.HasIndex(f => new { f.OrgId, f.CreatedAt });
        });
    }
}
