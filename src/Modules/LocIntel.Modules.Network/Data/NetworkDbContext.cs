using LocIntel.Modules.Network.Bulletins;
using LocIntel.Modules.Network.Shares;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Network.Data;

/// <summary>
/// Cross-org rows keyed off ONE single-owner table: share_access is the
/// caller's own standing, and every other filter here (and every RLS
/// policy in the migration) is one hop through it - no policy references
/// its own table, so nothing recurses.
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
            b.Property(x => x.ShareName).HasColumnName("share_name").HasMaxLength(200);
            b.Property(x => x.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.InvitedBy).HasColumnName("invited_by");
            b.Property(x => x.JoinedAt).HasColumnName("joined_at");
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
        // owner plus every non-removed member (Platform owner-and-recipients shape);
        // a removed member keeps its share_access row for the trail but loses access
        AddOwnerAndRecipientsFilter<Share>(
            modelBuilder,
            s =>
                Access.Any(a =>
                    a.ShareId == s.Id
                    && a.OrgId == CurrentOrg
                    && a.Status != MembershipStatus.Removed
                )
        );

        modelBuilder.Entity<ShareMember>(b =>
        {
            b.ToTable("share_members");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.ShareId).HasColumnName("share_id");
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.OrgName).HasColumnName("org_name").HasMaxLength(200);
            b.Property(x => x.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.JoinedAt).HasColumnName("joined_at");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => new { x.ShareId, x.OrgId }).IsUnique();
            b.HasQueryFilter(
                TenantFilter,
                m =>
                    Access.Any(a =>
                        a.ShareId == m.ShareId
                        && a.OrgId == CurrentOrg
                        && a.Status != MembershipStatus.Removed
                    )
            );
        });

        modelBuilder.Entity<SharedBulletin>(b =>
        {
            b.ToTable("shared_bulletins");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.ShareId).HasColumnName("share_id");
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
            b.Property(x => x.SourceEntityId).HasColumnName("source_entity_id");
            b.Property(x => x.PublishedBy).HasColumnName("published_by");
            b.Property(x => x.PublishedAt).HasColumnName("published_at");
            b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            b.Property(x => x.WithdrawnAt).HasColumnName("withdrawn_at");
            b.HasIndex(x => new { x.ShareId, x.PublishedAt });
            b.HasQueryFilter(
                TenantFilter,
                x =>
                    x.PublisherOrgId == CurrentOrg
                    || Access.Any(a =>
                        a.ShareId == x.ShareId
                        && a.OrgId == CurrentOrg
                        && a.Status == MembershipStatus.Active
                    )
            );
        });
    }
}
