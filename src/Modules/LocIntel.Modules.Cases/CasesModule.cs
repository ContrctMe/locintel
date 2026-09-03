using LocIntel.Modules.Cases.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.Modules.Cases;

public static class CasesModule
{
    public static IServiceCollection AddCasesModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<CasesDbContext>("cases");
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, CasesExporter>();
        return services;
    }
}
