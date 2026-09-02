using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LocIntel.Modules.Checklists.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Checklists;

public static class ChecklistsModule
{
    public static IServiceCollection AddChecklistsModule(this IServiceCollection services)
    {
        services.AddDbContextWithWolverineIntegration<ChecklistsDbContext>(
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
                            npgsql.MigrationsHistoryTable("__ef_migrations_history", "checklists")
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
