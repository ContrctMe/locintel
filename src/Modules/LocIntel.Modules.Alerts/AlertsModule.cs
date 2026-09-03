using LocIntel.Modules.Alerts.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.Modules.Alerts;

public static class AlertsModule
{
    public static IServiceCollection AddAlertsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<AlertsDbContext>("alerts");
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, AlertsExporter>();
        return services;
    }
}
