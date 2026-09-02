using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Modules.Tenancy.Organizations;
using LocIntel.Modules.Tenancy.Sites;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Tenancy;

public static class TenancyModule
{
    /// <summary>
    /// Module registration: DbContext resolves its connection through the
    /// region resolver per scope - no ambient connection string (ADR 35).
    /// </summary>
    public static IServiceCollection AddTenancyModule(
        this IServiceCollection services,
        bool runBackgroundWork = false
    )
    {
        if (runBackgroundWork)
        {
            services.AddHostedService<HorizonRollService>();
            services.AddHostedService<Organizations.OrgClosureService>();
        }

        services.AddDbContextWithWolverineIntegration<TenancyDbContext>(
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
                        npgsql =>
                            npgsql.MigrationsHistoryTable("__ef_migrations_history", "tenancy")
                    )
                    .AddInterceptors(
                        TenantSessionInterceptor.Instance,
                        sp.GetRequiredService<LocIntel.Platform.Audit.AuditSaveChangesInterceptor>()
                    );
            }
        );
        services.AddScoped<IOrganizationLookup, OrganizationLookup>();
        services.AddScoped<ISiteLookup, SiteLookup>();
        services.AddScoped<ISiteDirectory, Sites.SiteDirectory>();
        services.AddScoped<IEntitlementUsageProbe, MaxSitesProbe>();
        services.AddScoped<IEntitlementUsageProbe, HierarchyDepthProbe>();
        services.AddScoped<IOrgDataExporter, Organizations.TenancyExporter>();
        return services;
    }
}
