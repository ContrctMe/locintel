using LocIntel.Modules.Entities.Entities;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Entities.Data;

public sealed class EntitiesDbContext(
    DbContextOptions<EntitiesDbContext> options,
    ITenantContext tenant
) : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "entities";

    public DbSet<Entity> Entities => Set<Entity>();
    public DbSet<EntityIncidentLink> Links => Set<EntityIncidentLink>();
    public DbSet<EntityAccessGrant> Grants => Set<EntityAccessGrant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("ltree");

        modelBuilder.Entity<Entity>(b =>
        {
            b.ToTable("entities");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);
            b.Property(x => x.Aliases).HasColumnName("aliases");
            b.Property(x => x.DescriptorsJson).HasColumnName("descriptors").HasColumnType("jsonb");
            b.Property(x => x.Summary).HasColumnName("summary");
            b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            b.Property(x => x.LegalHold).HasColumnName("legal_hold");
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            b.HasIndex(x => new { x.OrgId, x.Kind });
            b.HasIndex(x => new { x.OrgId, x.ExpiresAt });
        });

        modelBuilder.Entity<EntityIncidentLink>(b =>
        {
            b.ToTable("incident_links");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.EntityId).HasColumnName("entity_id");
            b.Property(x => x.IncidentId).HasColumnName("incident_id");
            b.Property(x => x.SiteId).HasColumnName("site_id");
            b.Property(x => x.Path).HasColumnName("path");
            b.Property(x => x.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
            b.Property(x => x.LinkedBy).HasColumnName("linked_by");
            b.Property(x => x.LinkedAt).HasColumnName("linked_at");
            b.HasIndex(x => new { x.EntityId, x.IncidentId }).IsUnique();
            b.HasIndex(x => new { x.OrgId, x.IncidentId });
            b.HasIndex(x => x.Path).HasMethod("gist");
        });

        modelBuilder.Entity<EntityAccessGrant>(b =>
        {
            b.ToTable("access_grants");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.EntityId).HasColumnName("entity_id");
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.GrantedBy).HasColumnName("granted_by");
            b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500);
            b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => new
            {
                x.OrgId,
                x.UserId,
                x.EntityId,
            });
        });
    }
}
