using LocIntel.Platform.Audit;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Platform.Data;

/// <summary>
/// How a module's persistence is built - ONE place (architecture review,
/// candidate 3). Seven modules carried this block verbatim, and the fixture
/// five more; the only facts that varied were the schema (which the module
/// catalog already holds) and whether the audit interceptor applies. The
/// region rule (ADR 35: v1 single-region; multi-region moves connection
/// selection to a per-scope interceptor) now changes here, not in twelve
/// files.
/// </summary>
public static class ModulePersistence
{
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        string schema,
        bool audited = true
    )
        where TContext : ModuleDbContext
    {
        services.AddDbContextWithWolverineIntegration<TContext>(
            (sp, options) =>
            {
                // Options are SINGLETON: never resolve scoped services here (dev
                // scope-validation rejects it, and it would freeze the first
                // request's region).
                var regions = sp.GetRequiredService<IRegionDataSources>();
                var builder = options.UseNpgsql(
                    regions.For(RegionId.Default),
                    npgsql => Configure(npgsql, schema, typeof(TContext))
                );
                if (audited)
                    builder.AddInterceptors(
                        TenantSessionInterceptor.Instance,
                        sp.GetRequiredService<AuditSaveChangesInterceptor>()
                    );
                else
                    // the audit module's own tables are the sink, not a source
                    builder.AddInterceptors(TenantSessionInterceptor.Instance);
            }
        );
        return services;
    }

    /// <summary>
    /// The provider options every module context gets, wherever it is built
    /// (host, fixtures, round-trip test): its own migration history table,
    /// and the NetTopologySuite plugin only for a <see cref="SpatialModuleAttribute"/>
    /// context (ADR 50) - the plugin adds the postgis extension to any model it
    /// touches, so enabling it globally would leave every other module with
    /// pending model changes.
    /// </summary>
    public static void Configure(
        Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql,
        string schema,
        Type contextType
    )
    {
        npgsql.MigrationsHistoryTable("__ef_migrations_history", schema);
        if (contextType.IsDefined(typeof(SpatialModuleAttribute), inherit: false))
            npgsql.UseNetTopologySuite();
    }
}
