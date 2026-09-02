using LocIntel.Platform.Audit;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Audit.Data;

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "audit";

    /// <summary>Owns the sink table; must not diff its own writes.</summary>
    public override bool AuditsOwnChanges => false;

    public DbSet<AuditChangeLog> Changes => Set<AuditChangeLog>();
    public DbSet<DomainLogEntry> DomainEvents => Set<DomainLogEntry>();
    public DbSet<AuthzLogEntry> AuthzDecisions => Set<AuthzLogEntry>();
    public DbSet<AccessLogEntry> Accesses => Set<AccessLogEntry>();
    public DbSet<OrgAuditConfig> Configs => Set<OrgAuditConfig>();
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // the real change_log table (everyone else maps it ExcludeFromMigrations)
        modelBuilder.Entity<AuditChangeLog>(b =>
        {
            b.ToTable("change_log");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(a => a.OrgId).HasColumnName("org_id");
            b.Property(a => a.ActorTier).HasColumnName("actor_tier").HasMaxLength(20);
            b.Property(a => a.ActorId).HasColumnName("actor_id");
            b.Property(a => a.ActorLabel).HasColumnName("actor_label").HasMaxLength(320);
            b.Property(a => a.SchemaName).HasColumnName("schema_name").HasMaxLength(63);
            b.Property(a => a.TableName).HasColumnName("table_name").HasMaxLength(63);
            b.Property(a => a.RowId).HasColumnName("row_id").HasMaxLength(300);
            b.Property(a => a.Operation).HasColumnName("operation").HasMaxLength(10);
            b.Property(a => a.Diff).HasColumnName("diff").HasColumnType("jsonb");
            b.Property(a => a.OccurredAt).HasColumnName("occurred_at");
            b.HasIndex(a => new { a.OrgId, a.OccurredAt });
        });

        modelBuilder.Entity<DomainLogEntry>(b =>
        {
            b.ToTable("domain_log");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(a => a.OrgId).HasColumnName("org_id");
            b.Property(a => a.ActorTier).HasColumnName("actor_tier").HasMaxLength(20);
            b.Property(a => a.ActorId).HasColumnName("actor_id");
            b.Property(a => a.EventName).HasColumnName("event_name").HasMaxLength(120);
            b.Property(a => a.Payload).HasColumnName("payload").HasColumnType("jsonb");
            b.Property(a => a.OccurredAt).HasColumnName("occurred_at");
            b.HasIndex(a => new { a.OrgId, a.OccurredAt });
        });

        modelBuilder.Entity<AuthzLogEntry>(b =>
        {
            b.ToTable("authz_log");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(a => a.OrgId).HasColumnName("org_id");
            b.Property(a => a.ActorTier).HasColumnName("actor_tier").HasMaxLength(20);
            b.Property(a => a.ActorId).HasColumnName("actor_id");
            b.Property(a => a.Action).HasColumnName("action").HasMaxLength(120);
            b.Property(a => a.Outcome).HasColumnName("outcome").HasMaxLength(20);
            b.Property(a => a.ScopeSummary).HasColumnName("scope_summary").HasMaxLength(200);
            b.Property(a => a.OccurredAt).HasColumnName("occurred_at");
            b.HasIndex(a => new { a.OrgId, a.OccurredAt });
        });

        modelBuilder.Entity<AccessLogEntry>(b =>
        {
            // partitioned by month on occurred_at (ADR 38 follow-up): the
            // partition key must be part of the primary key
            b.ToTable("access_log");
            b.HasKey(a => new { a.Id, a.OccurredAt });
            b.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(a => a.OrgId).HasColumnName("org_id");
            b.Property(a => a.ActorTier).HasColumnName("actor_tier").HasMaxLength(20);
            b.Property(a => a.ActorId).HasColumnName("actor_id");
            b.Property(a => a.Method).HasColumnName("method").HasMaxLength(10);
            b.Property(a => a.Path).HasColumnName("path").HasMaxLength(500);
            b.Property(a => a.StatusCode).HasColumnName("status_code");
            b.Property(a => a.OccurredAt).HasColumnName("occurred_at");
            b.HasIndex(a => new { a.OrgId, a.OccurredAt });
        });

        modelBuilder.Entity<WebhookEndpoint>(b =>
        {
            b.ToTable("webhook_endpoints");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.Url).HasColumnName("url").HasMaxLength(1000);
            b.Property(x => x.EncryptedSecret).HasColumnName("encrypted_secret");
            b.Property(x => x.Events).HasColumnName("events");
            b.Property(x => x.Active).HasColumnName("active");
            b.Property(x => x.PreviousEncryptedSecret).HasColumnName("previous_encrypted_secret");
            b.Property(x => x.PreviousSecretExpiresAt).HasColumnName("previous_secret_expires_at");
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.OrgId);
        });

        modelBuilder.Entity<WebhookDelivery>(b =>
        {
            b.ToTable("webhook_deliveries");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.EndpointId).HasColumnName("endpoint_id");
            b.Property(x => x.EventName).HasColumnName("event_name").HasMaxLength(120);
            b.Property(x => x.Attempt).HasColumnName("attempt");
            b.Property(x => x.StatusCode).HasColumnName("status_code");
            b.Property(x => x.Ok).HasColumnName("ok");
            b.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            b.HasIndex(x => new { x.EndpointId, x.OccurredAt });
        });

        modelBuilder.Entity<OrgAuditConfig>(b =>
        {
            b.ToTable("org_audit_config");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(c => c.OrgId).HasColumnName("org_id");
            b.Property(c => c.LogGrants).HasColumnName("log_grants");
            b.Property(c => c.LogReads).HasColumnName("log_reads");
            b.Property(c => c.UpdatedAt).HasColumnName("updated_at");
            b.HasIndex(c => c.OrgId).IsUnique();
        });
    }
}
