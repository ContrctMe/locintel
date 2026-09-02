using LocIntel.Modules.Cases.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Cases;

public static class CasesModule
{
    public static IServiceCollection AddCasesModule(this IServiceCollection services)
    {
        services.AddDbContextWithWolverineIntegration<CasesDbContext>(
            (sp, options) =>
            {
                // Options are SINGLETON: never resolve scoped services here (dev
                // scope-validation rejects it). v1 is single-region (ADR 35);
                // multi-region moves connection selection to a per-scope interceptor.
                var regions = sp.GetRequiredService<IRegionDataSources>();
                options
                    .UseNpgsql(
                        regions.For(RegionId.Default),
                        npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "cases")
                    )
                    .AddInterceptors(
                        TenantSessionInterceptor.Instance,
                        sp.GetRequiredService<LocIntel.Platform.Audit.AuditSaveChangesInterceptor>()
                    );
            }
        );
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, CasesExporter>();
        return services;
    }
}
