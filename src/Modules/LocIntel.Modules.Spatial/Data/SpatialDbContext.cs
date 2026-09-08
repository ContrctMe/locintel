using LocIntel.Modules.Spatial.Overlays;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Spatial.Data;

/// <summary>
/// The Spatial module's schema (ADR 50 §3): org-drawn overlays. It sits above
/// Tenancy on the ladder (ADR 37) and knows nodes only through
/// <see cref="LocIntel.Contracts.IHierarchyDirectory"/>.
/// </summary>
[SpatialModule] // overlay_features.geom is geography (ADR 50)
public sealed class SpatialDbContext(
    DbContextOptions<SpatialDbContext> options,
    ITenantContext tenant
) : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "spatial";

    public DbSet<OverlayLayer> Layers => Set<OverlayLayer>();
    public DbSet<OverlayFeature> Features => Set<OverlayFeature>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("ltree");
        modelBuilder.HasPostgresExtension("postgis");

        modelBuilder.Entity<OverlayLayer>(b =>
        {
            b.ToTable("overlay_layers");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
            b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(40);
            b.Property(x => x.Style).HasColumnName("style").HasColumnType("jsonb");
            b.Property(x => x.NodeId).HasColumnName("node_id");
            b.Property(x => x.HierarchyId).HasColumnName("hierarchy_id");
            b.Property(x => x.Path).HasColumnName("path");
            b.Property(x => x.Version).HasColumnName("version");
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            b.HasIndex(x => new { x.OrgId, x.Name });
            b.HasIndex(x => x.Path).HasMethod("gist");
        });

        modelBuilder.Entity<OverlayFeature>(b =>
        {
            b.ToTable("overlay_features");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.LayerId).HasColumnName("layer_id");
            // geography, never geometry, for stored data (ADR 50); geometry
            // appears only inside the tile query
            b.Property(x => x.Geom)
                .HasColumnName("geom")
                .HasColumnType("geography (geometry, 4326)");
            b.Property(x => x.Properties).HasColumnName("properties").HasColumnType("jsonb");
            b.Property(x => x.HierarchyId).HasColumnName("hierarchy_id");
            b.Property(x => x.Path).HasColumnName("path");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.Geom).HasMethod("gist");
            b.HasIndex(x => x.Path).HasMethod("gist");
            b.HasIndex(x => new { x.OrgId, x.LayerId });
            b.HasOne<OverlayLayer>()
                .WithMany()
                .HasForeignKey(x => x.LayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
