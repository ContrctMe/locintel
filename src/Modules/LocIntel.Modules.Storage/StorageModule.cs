using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Storage;

public static class StorageModule
{
    public static IServiceCollection AddStorageModule(
        this IServiceCollection services,
        bool runBackgroundWork = false
    )
    {
        if (runBackgroundWork)
            services.AddHostedService<FileTrashService>();
        services.AddDbContextWithWolverineIntegration<StorageDbContext>(
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
                            npgsql.MigrationsHistoryTable("__ef_migrations_history", "storage")
                    )
                    .AddInterceptors(
                        TenantSessionInterceptor.Instance,
                        sp.GetRequiredService<LocIntel.Platform.Audit.AuditSaveChangesInterceptor>()
                    );
            }
        );
        services.AddScoped<LocIntel.Contracts.IStoredFileLookup, StoredFileLookup>();
        services.AddScoped<LocIntel.Contracts.Storage.ISignedFileAccess, SignedFileAccess>();
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, StorageExporter>();
        return services;
    }
}
