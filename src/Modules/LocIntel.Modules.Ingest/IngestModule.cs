using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LocIntel.Modules.Ingest.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Ingest;

public static class IngestModule
{
    public static IServiceCollection AddIngestModule(
        this IServiceCollection services,
        bool runBackgroundWork = false
    )
    {
        if (runBackgroundWork)
            services.AddHostedService<ConnectorScheduleService>();

        services.AddDbContextWithWolverineIntegration<IngestDbContext>(
            (sp, options) =>
            {
                // Options are SINGLETON: never resolve scoped services here (dev
                // scope-validation rejects it, and it would freeze the first
                // request's region). v1 is single-region (ADR 35); multi-region
                // moves connection selection to a per-scope interceptor.
                var regions = sp.GetRequiredService<IRegionDataSources>();
                options
                    .UseNpgsql(
                        regions.For(RegionId.Default),
                        npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "ingest")
                    )
                    .AddInterceptors(
                        TenantSessionInterceptor.Instance,
                        sp.GetRequiredService<LocIntel.Platform.Audit.AuditSaveChangesInterceptor>()
                    );
            }
        );
        services.AddScoped<StagingService>();
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, IngestExporter>();
        services.AddHttpClient("ingest-connector");
        return services;
    }
}
