using LocIntel.Modules.Spatial.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Spatial;

public static class SpatialModule
{
    public static IServiceCollection AddSpatialModule(this IServiceCollection services)
    {
        // persistence is built in ONE place (ModulePersistence, ADR 35): the
        // schema is the only fact a module supplies
        services.AddModuleDbContext<SpatialDbContext>("spatial");
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, SpatialExporter>();
        services.AddScoped<LocIntel.Contracts.IReportOverlaySource, Overlays.ReportOverlaySource>();
        return services;
    }
}
