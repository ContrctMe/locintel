using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddDbContextWithWolverineIntegration<IdentityDbContext>(
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
                            npgsql.MigrationsHistoryTable("__ef_migrations_history", "identity")
                    )
                    .AddInterceptors(
                        TenantSessionInterceptor.Instance,
                        sp.GetRequiredService<LocIntel.Platform.Audit.AuditSaveChangesInterceptor>()
                    );
            }
        );
        services.AddScoped<IOperatorContext, LocIntel.Modules.Identity.Access.OperatorContext>();
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, Users.IdentityExporter>();
        services.AddScoped<LocIntel.Contracts.IActorDirectory, Users.ActorDirectory>();
        return services;
    }
}
