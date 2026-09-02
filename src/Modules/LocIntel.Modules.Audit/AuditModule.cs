using LocIntel.Modules.Audit.Data;
using LocIntel.Platform.Audit;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Audit;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(
        this IServiceCollection services,
        bool runBackgroundWork = false
    )
    {
        services.AddDbContextWithWolverineIntegration<AuditDbContext>(
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
                        npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "audit")
                    )
                    .AddInterceptors(TenantSessionInterceptor.Instance);
            }
        );
        services.AddScoped<IAuditPolicyProvider, AuditPolicyService>();
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, AuditExporter>();
        services.AddScoped<LocIntel.Contracts.IAuditTrailExporter, AuditTrailExporter>();
        if (runBackgroundWork)
            services.AddHostedService<AuditRetentionService>();
        return services;
    }
}
