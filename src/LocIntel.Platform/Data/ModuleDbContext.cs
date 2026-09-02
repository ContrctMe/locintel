using System.Linq.Expressions;
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
/// TenantSessionInterceptor + database policies) backstops a disabled or
/// forgotten tenant filter: fail closed, not leak.
///
/// IMPORTANT: the tenant filter references CurrentOrg on the *context instance*
/// - EF rewrites that per instance despite the cached model. Never capture the
/// ITenantContext object in a filter expression; the first request's tenant
/// would be baked into the cached model.
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
            if (typeof(ITwoPartyScoped).IsAssignableFrom(entity.ClrType))
                InvokeFilter(nameof(AddTwoPartyFilter), entity.ClrType, modelBuilder);
            if (typeof(IRequiredCounterpartyScoped).IsAssignableFrom(entity.ClrType))
                InvokeFilter(nameof(AddRequiredCounterpartyFilter), entity.ClrType, modelBuilder);
            if (typeof(IPublishedCatalogScoped).IsAssignableFrom(entity.ClrType))
                InvokeFilter(nameof(AddPublishedCatalogFilter), entity.ClrType, modelBuilder);
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
        where TEntity : class, IOrgScoped =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(TenantFilter, e => e.OrgId == CurrentOrg);

    // both sides see the row; the SAME filter name as the single-owner one,
    // so a fork cannot accidentally stack two tenant filters on one entity
    private void AddTwoPartyFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITwoPartyScoped =>
        modelBuilder
            .Entity<TEntity>()
            .HasQueryFilter(
                TenantFilter,
                e => e.OrgId == CurrentOrg || e.CounterpartyOrgId == CurrentOrg
            );

    // same either-side predicate as the nullable shape; separate only because
    // the property types differ
    private void AddRequiredCounterpartyFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IRequiredCounterpartyScoped =>
        modelBuilder
            .Entity<TEntity>()
            .HasQueryFilter(
                TenantFilter,
                e => e.OrgId == CurrentOrg || e.CounterpartyOrgId == CurrentOrg
            );

    private void AddPublishedCatalogFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IPublishedCatalogScoped =>
        modelBuilder
            .Entity<TEntity>()
            .HasQueryFilter(TenantFilter, e => e.Published || e.OrgId == CurrentOrg);

    /// <summary>
    /// The query-filter half of <c>EnableRecipientListRls</c> (the policy is
    /// the enforcement; this keeps LINQ reads agreeing with it). The caller
    /// supplies the side-table lookup, because only it knows the foreign key:
    ///
    /// <code>
    /// AddRecipientListFilter&lt;Request&gt;(
    ///     modelBuilder,
    ///     r => Recipients.Any(x => x.RequestId == r.Id &amp;&amp; x.CounterpartyOrgId == CurrentOrg));
    /// </code>
    ///
    /// Gate the lookup on the recipient row's own state where membership can
    /// lapse - `&amp;&amp; a.Status != MembershipStatus.Removed` - because a
    /// removal usually KEEPS the row for the audit trail, so listing alone
    /// cannot mean access.
    ///
    /// Read-only unless the policy was written with
    /// <c>writableByRecipient</c>; the filter governs reads either way.
    /// </summary>
    protected void AddRecipientListFilter<TEntity>(
        ModelBuilder modelBuilder,
        Expression<Func<TEntity, bool>> visibleToRecipient
    )
        where TEntity : class, ITwoPartyScoped
    {
        var entity = Expression.Parameter(typeof(TEntity), "e");
        var standard =
            (Expression<Func<TEntity, bool>>)(
                e => e.OrgId == CurrentOrg || e.CounterpartyOrgId == CurrentOrg
            );
        var combined = Expression.OrElse(
            new Rebind(standard.Parameters[0], entity).Visit(standard.Body)!,
            new Rebind(visibleToRecipient.Parameters[0], entity).Visit(visibleToRecipient.Body)!
        );
        modelBuilder
            .Entity<TEntity>()
            .HasQueryFilter(TenantFilter, Expression.Lambda<Func<TEntity, bool>>(combined, entity));
    }

    /// <summary>
    /// The same shape for a SINGLE-OWNER parent - a share and its members,
    /// where there is no counterparty. Separate name because C# cannot
    /// overload on the type constraint alone.
    /// </summary>
    protected void AddOwnerAndRecipientsFilter<TEntity>(
        ModelBuilder modelBuilder,
        Expression<Func<TEntity, bool>> visibleToRecipient
    )
        where TEntity : class, IOrgScoped
    {
        var entity = Expression.Parameter(typeof(TEntity), "e");
        var owner = (Expression<Func<TEntity, bool>>)(e => e.OrgId == CurrentOrg);
        var combined = Expression.OrElse(
            new Rebind(owner.Parameters[0], entity).Visit(owner.Body)!,
            new Rebind(visibleToRecipient.Parameters[0], entity).Visit(visibleToRecipient.Body)!
        );
        modelBuilder
            .Entity<TEntity>()
            .HasQueryFilter(TenantFilter, Expression.Lambda<Func<TEntity, bool>>(combined, entity));
    }

    /// <summary>Points two lambdas at one parameter so their bodies can be OR-ed.</summary>
    private sealed class Rebind(ParameterExpression from, ParameterExpression to)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
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
