using LocIntel.Modules.Entities.Data;
using LocIntel.Modules.Entities.Retention;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.Extensions.DependencyInjection;

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

        services.AddModuleDbContext<EntitiesDbContext>("entities");
        services.AddScoped<
            LocIntel.Contracts.Entities.IEntityDirectory,
            Entities.EntityDirectory
        >();
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, EntitiesExporter>();
        return services;
    }
}
