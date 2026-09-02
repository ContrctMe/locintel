using LocIntel.Modules.Checklists.Checklists;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Checklists.Data;

public sealed class ChecklistsDbContext(
    DbContextOptions<ChecklistsDbContext> options,
    ITenantContext tenant
) : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "checklists";

    public DbSet<ChecklistTemplate> Templates => Set<ChecklistTemplate>();
    public DbSet<ChecklistItemCheck> Checks => Set<ChecklistItemCheck>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ChecklistTemplate>(b =>
        {
            b.ToTable("templates");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
            b.Property(x => x.Items).HasColumnName("items");
            b.Property(x => x.ScopePath).HasColumnName("scope_path").HasMaxLength(500);
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.OrgId);
        });

        modelBuilder.Entity<ChecklistItemCheck>(b =>
        {
            b.ToTable("item_checks");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.TemplateId).HasColumnName("template_id");
            b.Property(x => x.SiteId).HasColumnName("site_id");
            b.Property(x => x.BusinessDate).HasColumnName("business_date");
            b.Property(x => x.ItemIndex).HasColumnName("item_index");
            b.Property(x => x.CheckedBy).HasColumnName("checked_by");
            b.Property(x => x.CheckedAt).HasColumnName("checked_at");
            b.HasIndex(x => new
                {
                    x.TemplateId,
                    x.SiteId,
                    x.BusinessDate,
                    x.ItemIndex,
                })
                .IsUnique();
            b.HasIndex(x => new
            {
                x.OrgId,
                x.SiteId,
                x.BusinessDate,
            });
        });
    }
}
