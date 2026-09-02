using LocIntel.Contracts.Incidents;
using LocIntel.Modules.Incidents.Data;
using LocIntel.Modules.Incidents.Incidents;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Incidents;

public static class IncidentsModule
{
    public static IServiceCollection AddIncidentsModule(this IServiceCollection services)
    {
        services.AddDbContextWithWolverineIntegration<IncidentsDbContext>(
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
                            npgsql.MigrationsHistoryTable("__ef_migrations_history", "incidents")
                    )
                    .AddInterceptors(
                        TenantSessionInterceptor.Instance,
                        sp.GetRequiredService<LocIntel.Platform.Audit.AuditSaveChangesInterceptor>()
                    );
            }
        );
        services.AddScoped<IIncidentDirectory, IncidentDirectory>();
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, IncidentsExporter>();
        return services;
    }
}
