using LocIntel.Contracts.Incidents;
using LocIntel.Modules.Incidents.Data;
using LocIntel.Modules.Incidents.Incidents;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.Modules.Incidents;

public static class IncidentsModule
{
    public static IServiceCollection AddIncidentsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<IncidentsDbContext>("incidents");
        services.AddScoped<IIncidentDirectory, IncidentDirectory>();
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, IncidentsExporter>();
        return services;
    }
}
