using LocIntel.Modules.Network.Bulletins;
using LocIntel.Modules.Network.Shares;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Network.Data;

/// <summary>
/// Every row has one owner (ADR 48): the owner holds the share and its
/// roster; each member holds its own access row (a projection of the share)
/// and its own copies of bulletins; a publisher holds what it published.
/// Nothing here is read across the org line.
/// </summary>
public sealed class NetworkDbContext(
    DbContextOptions<NetworkDbContext> options,
    ITenantContext tenant
) : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "network";

    public DbSet<Share> Shares => Set<Share>();
    public DbSet<ShareAccess> Access => Set<ShareAccess>();
    public DbSet<ShareMember> Members => Set<ShareMember>();
    public DbSet<SharedBulletin> Bulletins => Set<SharedBulletin>();
    public DbSet<SharedBulletinCopy> BulletinCopies => Set<SharedBulletinCopy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ShareAccess>(b =>
        {
            b.ToTable("share_access");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.ShareId).HasColumnName("share_id");
            b.Property(x => x.OwnerOrgId).HasColumnName("owner_org_id");
            b.Property(x => x.ShareName).HasColumnName("share_name").HasMaxLength(200);
            b.Property(x => x.Description).HasColumnName("description");
            b.Property(x => x.OwnerName).HasColumnName("owner_name").HasMaxLength(200);
            b.Property(x => x.ShareStatus)
                .HasColumnName("share_status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.InvitedBy).HasColumnName("invited_by");
            b.Property(x => x.JoinedAt).HasColumnName("joined_at");
            b.Property(x => x.RosterJson).HasColumnName("roster").HasColumnType("jsonb");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => new { x.ShareId, x.OrgId }).IsUnique();
        });

        modelBuilder.Entity<Share>(b =>
        {
            b.ToTable("shares");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("owner_org_id");
            b.Property(x => x.OwnerName).HasColumnName("owner_name").HasMaxLength(200);
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
            b.Property(x => x.Description).HasColumnName("description");
            b.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.HasIndex(x => x.OrgId);
        });

        modelBuilder.Entity<ShareMember>(b =>
        {
            b.ToTable("share_members");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.ShareId).HasColumnName("share_id");
            b.Property(x => x.MemberOrgId).HasColumnName("member_org_id");
            b.Property(x => x.OrgName).HasColumnName("org_name").HasMaxLength(200);
            b.Property(x => x.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.JoinedAt).HasColumnName("joined_at");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => new { x.ShareId, x.MemberOrgId }).IsUnique();
        });

        modelBuilder.Entity<SharedBulletin>(b =>
        {
            b.ToTable("shared_bulletins");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.ShareId).HasColumnName("share_id");
            b.Property(x => x.OrgId).HasColumnName("publisher_org_id");
            b.Property(x => x.PublisherName).HasColumnName("publisher_name").HasMaxLength(200);
            b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Severity)
                .HasColumnName("severity")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
            b.Property(x => x.Body).HasColumnName("body");
            b.Property(x => x.EntityKind).HasColumnName("entity_kind").HasMaxLength(20);
            b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);
            b.Property(x => x.Aliases).HasColumnName("aliases");
            b.Property(x => x.DescriptorsJson).HasColumnName("descriptors").HasColumnType("jsonb");
            b.Property(x => x.Areas).HasColumnName("areas");
            b.Property(x => x.SourceEntityId).HasColumnName("source_entity_id");
            b.Property(x => x.PublishedBy).HasColumnName("published_by");
            b.Property(x => x.PublishedAt).HasColumnName("published_at");
            b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            b.Property(x => x.WithdrawnAt).HasColumnName("withdrawn_at");
            b.HasIndex(x => new { x.ShareId, x.PublishedAt });
        });

        modelBuilder.Entity<SharedBulletinCopy>(b =>
        {
            b.ToTable("shared_bulletin_copies");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.BulletinId).HasColumnName("bulletin_id");
            b.Property(x => x.ShareId).HasColumnName("share_id");
            b.Property(x => x.ShareName).HasColumnName("share_name").HasMaxLength(200);
            b.Property(x => x.PublisherOrgId).HasColumnName("publisher_org_id");
            b.Property(x => x.PublisherName).HasColumnName("publisher_name").HasMaxLength(200);
            b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Severity)
                .HasColumnName("severity")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
            b.Property(x => x.Body).HasColumnName("body");
            b.Property(x => x.EntityKind).HasColumnName("entity_kind").HasMaxLength(20);
            b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);
            b.Property(x => x.Aliases).HasColumnName("aliases");
            b.Property(x => x.DescriptorsJson).HasColumnName("descriptors").HasColumnType("jsonb");
            b.Property(x => x.Areas).HasColumnName("areas");
            b.Property(x => x.PublishedAt).HasColumnName("published_at");
            b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            b.Property(x => x.WithdrawnAt).HasColumnName("withdrawn_at");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => new { x.OrgId, x.BulletinId }).IsUnique();
            b.HasIndex(x => new { x.ShareId, x.PublishedAt });
        });
    }
}
