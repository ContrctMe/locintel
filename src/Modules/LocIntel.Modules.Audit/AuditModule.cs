using LocIntel.Modules.Audit.Data;
using LocIntel.Platform.Audit;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Audit;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(
        this IServiceCollection services,
        bool runBackgroundWork = false
    )
    {
        services.AddModuleDbContext<AuditDbContext>("audit", audited: false);
        services.AddScoped<IAuditPolicyProvider, AuditPolicyService>();
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, AuditExporter>();
        services.AddScoped<LocIntel.Contracts.IAuditTrailExporter, AuditTrailExporter>();
        if (runBackgroundWork)
            services.AddHostedService<AuditRetentionService>();
        return services;
    }
}
