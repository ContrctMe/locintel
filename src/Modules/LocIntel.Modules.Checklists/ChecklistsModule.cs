using LocIntel.Modules.Checklists.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Checklists;

public static class ChecklistsModule
{
    public static IServiceCollection AddChecklistsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<ChecklistsDbContext>("checklists");
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, ChecklistsExporter>();

        return services;
    }
}
