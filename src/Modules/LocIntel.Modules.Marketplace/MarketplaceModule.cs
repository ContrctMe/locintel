using LocIntel.Modules.Marketplace.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Marketplace;

public static class MarketplaceModule
{
    public static IServiceCollection AddMarketplaceModule(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        bool runBackgroundWork = false
    )
    {
        services.Configure<Marketplace.MarketplaceOptions>(configuration.GetSection("Marketplace"));
        if (runBackgroundWork)
            services.AddHostedService<Requests.RequestSlaService>();
        services.AddDbContextWithWolverineIntegration<MarketplaceDbContext>(
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
                            npgsql.MigrationsHistoryTable("__ef_migrations_history", "marketplace")
                    )
                    .AddInterceptors(
                        TenantSessionInterceptor.Instance,
                        sp.GetRequiredService<LocIntel.Platform.Audit.AuditSaveChangesInterceptor>()
                    );
            }
        );
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, MarketplaceExporter>();
        return services;
    }
}
