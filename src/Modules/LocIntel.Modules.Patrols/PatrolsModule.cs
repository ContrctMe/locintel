using LocIntel.Modules.Patrols.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.Modules.Patrols;

public static class PatrolsModule
{
    public static IServiceCollection AddPatrolsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<PatrolsDbContext>("patrols");
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, PatrolsExporter>();
        return services;
    }
}
