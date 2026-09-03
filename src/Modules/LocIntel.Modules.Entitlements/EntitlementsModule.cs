using LocIntel.Modules.Entitlements.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Entitlements;

public static class EntitlementsModule
{
    public static IServiceCollection AddEntitlementsModule(
        this IServiceCollection services,
        bool runBackgroundWork = false
    )
    {
        services.AddModuleDbContext<EntitlementsDbContext>("entitlements");
        // both by TYPE: Wolverine codegen inlines type registrations (no service location)
        services.AddScoped<EntitlementsService>();
        services.AddScoped<IEntitlements, EntitlementsService>();
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, EntitlementsExporter>();
        if (runBackgroundWork)
            services.AddHostedService<MeterCompactionService>();
        return services;
    }
}
