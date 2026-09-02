using LocIntel.Modules.Identity.Access;
using LocIntel.Modules.Identity.Auth;
using LocIntel.Modules.Identity.Users;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Identity.Data;

public sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options,
    ITenantContext tenant
) : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "identity";

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<OrgDirectoryEntry> OrgDirectory => Set<OrgDirectoryEntry>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RoleGrant> RoleGrants => Set<RoleGrant>();
    public DbSet<MembershipRole> MembershipRoles => Set<MembershipRole>();
    public DbSet<GrantException> GrantExceptions => Set<GrantException>();
    public DbSet<InvitedRole> InvitedRoles => Set<InvitedRole>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<EmailSuppression> EmailSuppressions => Set<EmailSuppression>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(b =>
        {
            b.ToTable("users");
            b.HasKey(u => u.Id);
            b.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(u => u.Provider).HasColumnName("provider").HasMaxLength(40);
            b.Property(u => u.Subject).HasColumnName("subject").HasMaxLength(200);
            b.Property(u => u.Email).HasColumnName("email").HasMaxLength(320);
            b.Property(u => u.Name).HasColumnName("name").HasMaxLength(200);
            b.Property(u => u.CreatedAt).HasColumnName("created_at");
            b.HasIndex(u => new { u.Provider, u.Subject }).IsUnique();
            b.HasIndex(u => u.Email);
        });

        modelBuilder.Entity<ApiKey>(b =>
        {
            b.ToTable("api_keys");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(120);
            b.Property(x => x.SecretHash).HasColumnName("secret_hash").HasMaxLength(64);
            b.Property(x => x.Prefix).HasColumnName("prefix").HasMaxLength(16);
            b.Property(x => x.RoleId).HasColumnName("role_id");
            b.Property(x => x.ScopePath).HasColumnName("scope_path").HasMaxLength(500);
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
            b.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            b.HasIndex(x => x.SecretHash).IsUnique();
            b.HasIndex(x => x.OrgId);
        });

        modelBuilder.Entity<EmailSuppression>(b =>
        {
            b.ToTable("email_suppressions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.Email).HasColumnName("email").HasMaxLength(320);
            b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(40);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<Contact>(b =>
        {
            b.ToTable("contacts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.Email).HasColumnName("email").HasMaxLength(320);
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            b.HasIndex(x => new { x.OrgId, x.Email }).IsUnique();
        });

        modelBuilder.Entity<UserSession>(b =>
        {
            b.ToTable("user_sessions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.UserAgent).HasColumnName("user_agent").HasMaxLength(200);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            b.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<Membership>(b =>
        {
            b.ToTable("memberships");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(m => m.UserId).HasColumnName("user_id");
            b.Property(m => m.OrgId).HasColumnName("org_id");
            b.Property(m => m.CreatedAt).HasColumnName("created_at");
            b.HasIndex(m => new { m.UserId, m.OrgId }).IsUnique();
            b.HasIndex(m => m.OrgId);
        });

        modelBuilder.Entity<OrgDirectoryEntry>(b =>
        {
            b.ToTable("org_directory");
            b.HasKey(d => d.OrgId);
            b.Property(d => d.OrgId).HasColumnName("org_id").ValueGeneratedNever();
            b.Property(d => d.Name).HasColumnName("name").HasMaxLength(200);
            b.Property(d => d.Slug).HasColumnName("slug").HasMaxLength(80);
            b.Property(d => d.Region).HasColumnName("region").HasMaxLength(40);
            b.Property(d => d.ExternalId).HasColumnName("external_id").HasMaxLength(120);
            b.Property(d => d.Status).HasColumnName("status").HasMaxLength(20);
            b.Property(d => d.IsPlatform).HasColumnName("is_platform");
            b.Property(d => d.SyncedAt).HasColumnName("synced_at");
            b.HasIndex(d => d.Slug);
            b.HasIndex(d => d.ExternalId);
        });

        modelBuilder.Entity<Role>(b =>
        {
            b.ToTable("roles");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(r => r.OrgId).HasColumnName("org_id");
            b.Property(r => r.Name).HasColumnName("name").HasMaxLength(120);
            b.HasIndex(r => new { r.OrgId, r.Name }).IsUnique();
        });

        modelBuilder.Entity<RoleGrant>(b =>
        {
            b.ToTable("role_grants");
            b.HasKey(g => g.Id);
            b.Property(g => g.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(g => g.OrgId).HasColumnName("org_id");
            b.Property(g => g.RoleId).HasColumnName("role_id");
            b.Property(g => g.Domain).HasColumnName("domain").HasMaxLength(60);
            b.Property(g => g.Action).HasColumnName("action").HasMaxLength(60);
            b.HasIndex(g => g.RoleId);
        });

        modelBuilder.Entity<MembershipRole>(b =>
        {
            b.ToTable("membership_roles");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(m => m.OrgId).HasColumnName("org_id");
            b.Property(m => m.MembershipId).HasColumnName("membership_id");
            b.Property(m => m.RoleId).HasColumnName("role_id");
            b.Property(m => m.ScopePath).HasColumnName("scope_path").HasMaxLength(2000);
            b.HasIndex(m => m.MembershipId);
        });

        modelBuilder.Entity<InvitedRole>(b =>
        {
            b.ToTable("invited_roles");
            b.HasKey(i => i.Id);
            b.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(i => i.OrgId).HasColumnName("org_id");
            b.Property(i => i.Email).HasColumnName("email").HasMaxLength(320);
            b.Property(i => i.RoleId).HasColumnName("role_id");
            b.Property(i => i.InvitationExternalId)
                .HasColumnName("invitation_external_id")
                .HasMaxLength(120);
            b.Property(i => i.InvitedBy).HasColumnName("invited_by");
            b.Property(i => i.CreatedAt).HasColumnName("created_at");
            b.HasIndex(i => new { i.OrgId, i.Email }).IsUnique();
        });

        modelBuilder.Entity<GrantException>(b =>
        {
            b.ToTable("grant_exceptions");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(e => e.OrgId).HasColumnName("org_id");
            b.Property(e => e.UserId).HasColumnName("user_id");
            b.Property(e => e.Domain).HasColumnName("domain").HasMaxLength(60);
            b.Property(e => e.Action).HasColumnName("action").HasMaxLength(60);
            b.Property(e => e.ScopePath).HasColumnName("scope_path").HasMaxLength(2000);
            b.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(500);
            b.Property(e => e.GrantedBy).HasColumnName("granted_by");
            b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            b.Property(e => e.CreatedAt).HasColumnName("created_at");
            b.HasIndex(e => new
            {
                e.UserId,
                e.OrgId,
                e.ExpiresAt,
            });
        });
    }
}
