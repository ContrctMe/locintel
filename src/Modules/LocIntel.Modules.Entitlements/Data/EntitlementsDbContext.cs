using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Entitlements.Data;

public sealed class EntitlementsDbContext(
    DbContextOptions<EntitlementsDbContext> options,
    ITenantContext tenant
) : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "entitlements";

    public DbSet<OrgEntitlement> OrgEntitlements => Set<OrgEntitlement>();
    public DbSet<EntitlementException> Exceptions => Set<EntitlementException>();
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();
    public DbSet<MeterRollup> Rollups => Set<MeterRollup>();
    public DbSet<OrgSubscription> Subscriptions => Set<OrgSubscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OrgSubscription>(b =>
        {
            b.ToTable("org_subscriptions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.PlanId).HasColumnName("plan_id").HasMaxLength(80);
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
            b.Property(x => x.CustomerRef).HasColumnName("customer_ref").HasMaxLength(200);
            b.Property(x => x.SubscriptionRef).HasColumnName("subscription_ref").HasMaxLength(200);
            b.Property(x => x.CurrentPeriodEnd).HasColumnName("current_period_end");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.HasIndex(x => x.OrgId).IsUnique();
        });

        modelBuilder.Entity<OrgEntitlement>(b =>
        {
            b.ToTable("org_entitlements");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(e => e.OrgId).HasColumnName("org_id");
            b.Property(e => e.Code).HasColumnName("code").HasMaxLength(80);
            b.Property(e => e.Value).HasColumnName("value").HasMaxLength(200);
            b.Property(e => e.Source).HasColumnName("source").HasMaxLength(40);
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            b.HasIndex(e => new { e.OrgId, e.Code }).IsUnique();
        });

        modelBuilder.Entity<EntitlementException>(b =>
        {
            b.ToTable("entitlement_exceptions");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(e => e.OrgId).HasColumnName("org_id");
            b.Property(e => e.Code).HasColumnName("code").HasMaxLength(80);
            b.Property(e => e.Value).HasColumnName("value").HasMaxLength(200);
            b.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(500);
            b.Property(e => e.GrantedBy).HasColumnName("granted_by");
            b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            b.Property(e => e.CreatedAt).HasColumnName("created_at");
            b.HasIndex(e => new
            {
                e.OrgId,
                e.Code,
                e.ExpiresAt,
            });
        });

        modelBuilder.Entity<UsageEvent>(b =>
        {
            b.ToTable("usage_events");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(e => e.OrgId).HasColumnName("org_id");
            b.Property(e => e.Code).HasColumnName("code").HasMaxLength(80);
            b.Property(e => e.Amount).HasColumnName("amount");
            b.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            b.HasIndex(e => new
            {
                e.OrgId,
                e.Code,
                e.OccurredAt,
            });
        });

        modelBuilder.Entity<MeterRollup>(b =>
        {
            b.ToTable("meter_rollups");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(r => r.OrgId).HasColumnName("org_id");
            b.Property(r => r.Code).HasColumnName("code").HasMaxLength(80);
            b.Property(r => r.PeriodMonth).HasColumnName("period_month");
            b.Property(r => r.Amount).HasColumnName("amount");
            b.Property(r => r.CompactedThrough).HasColumnName("compacted_through");
            b.HasIndex(r => new
                {
                    r.OrgId,
                    r.Code,
                    r.PeriodMonth,
                })
                .IsUnique();
        });
    }
}
