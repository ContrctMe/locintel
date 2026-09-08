using System.Reflection;
using LocIntel.Platform.Audit;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Platform.Data;

/// <summary>
/// Base DbContext for every module (ADR 17): one Postgres schema per module,
/// its own migration history in that schema, and by-convention behavior:
///  - "Tenant" named query filter on every IOrgScoped entity
///  - "SoftDelete" named query filter on every ISoftDeletable entity
/// Named filters (EF 10) can be disabled independently - e.g. an admin restore
/// screen disables SoftDelete but must never disable Tenant. RLS (set by
/// TenantSessionInterceptor, SET LOCAL per command + database policies) backstops a disabled or
/// forgotten tenant filter: fail closed, not leak.
///
/// IMPORTANT: EF binds the tenant filter's typed context reference to the
/// current instance. Never capture ITenantContext (which freezes the tenant)
/// or a live DbContext (which the cached model would retain after disposal).
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options, ITenantContext tenant)
    : DbContext(options)
{
    public const string TenantFilter = "Tenant";
    public const string SoftDeleteFilter = "SoftDelete";

    /// <summary>The module's Postgres schema, e.g. "tenancy".</summary>
    public abstract string ModuleSchema { get; }

    /// <summary>
    /// The audit module's own context turns this off: it maps the real table
    /// and must not diff its own sink writes (recursion).
    /// </summary>
    public virtual bool AuditsOwnChanges => true;

    public ITenantContext Tenant { get; } = tenant;

    /// <summary>
    /// Every SaveChanges runs in a transaction, even a one-statement one
    /// (ADR 53): the tenant variable is set when the transaction starts, and
    /// a write batch is never prefixed with it - EF addresses a batch's
    /// statements by position, and a leading SET would shift every index.
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Org for the tenant query filter. Empty when no tenant is set, which
    /// matches no rows: fail closed.
    /// </summary>
    public OrgId CurrentOrg => Tenant.OrgId ?? new OrgId(Guid.Empty);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ModuleSchema);

        if (AuditsOwnChanges)
        {
            // Shared audit sink (ADR 12/13): every module appends to the audit
            // schema's table inside its own transaction. The audit module owns
            // the table; excluded from this module's migrations.
            modelBuilder.Entity<AuditChangeLog>(b =>
            {
                b.ToTable("change_log", "audit", t => t.ExcludeFromMigrations());
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
            });
        }

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IOrgScoped).IsAssignableFrom(entity.ClrType))
                InvokeFilter(nameof(AddTenantFilter), entity.ClrType, modelBuilder);
            if (typeof(ISoftDeletable).IsAssignableFrom(entity.ClrType))
                InvokeFilter(nameof(AddSoftDeleteFilter), entity.ClrType, modelBuilder);
        }
    }

    private void InvokeFilter(string method, Type clrType, ModelBuilder modelBuilder) =>
        GetType()
            .BaseType!.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(clrType)
            .Invoke(this, [modelBuilder]);

    private void AddTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IOrgScoped
    {
        // EF replaces this typed reference with the context executing the query.
        // A live `this` would root its original host through EF's model cache.
        ModuleDbContext context = null!;
        modelBuilder
            .Entity<TEntity>()
            .HasQueryFilter(TenantFilter, e => e.OrgId == context.CurrentOrg);
    }

    private void AddSoftDeleteFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ISoftDeletable =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(SoftDeleteFilter, e => e.DeletedAt == null);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<OrgId>().HaveConversion<OrgIdConverter>();
        configurationBuilder.Properties<SiteId>().HaveConversion<SiteIdConverter>();
        configurationBuilder.Properties<RegionId>().HaveConversion<RegionIdConverter>();
    }
}

public sealed class OrgIdConverter()
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<OrgId, Guid>(
        v => v.Value,
        v => new OrgId(v)
    );

public sealed class SiteIdConverter()
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<SiteId, Guid>(
        v => v.Value,
        v => new SiteId(v)
    );

public sealed class RegionIdConverter()
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<RegionId, string>(
        v => v.Value,
        v => new RegionId(v)
    );
