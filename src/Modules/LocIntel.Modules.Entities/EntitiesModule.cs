using LocIntel.Modules.Entities.Data;
using LocIntel.Modules.Entities.Retention;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Entities;

public static class EntitiesModule
{
    public static IServiceCollection AddEntitiesModule(
        this IServiceCollection services,
        bool runBackgroundWork = false
    )
    {
        if (runBackgroundWork)
            services.AddHostedService<EntityRetentionService>();

        services.AddDbContextWithWolverineIntegration<EntitiesDbContext>(
            (sp, options) =>
            {
                // Options are SINGLETON: never resolve scoped services here (dev
                // scope-validation rejects it). v1 is single-region (ADR 35);
                // multi-region moves connection selection to a per-scope interceptor.
                var regions = sp.GetRequiredService<IRegionDataSources>();
                options
                    .UseNpgsql(
                        regions.For(RegionId.Default),
                        npgsql =>
                            npgsql.MigrationsHistoryTable("__ef_migrations_history", "entities")
                    )
                    .AddInterceptors(
                        TenantSessionInterceptor.Instance,
                        sp.GetRequiredService<LocIntel.Platform.Audit.AuditSaveChangesInterceptor>()
                    );
            }
        );
        return services;
    }
}
