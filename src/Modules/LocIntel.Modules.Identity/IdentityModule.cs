using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<IdentityDbContext>("identity");
        services.AddScoped<IOperatorContext, LocIntel.Modules.Identity.Access.OperatorContext>();
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, Users.IdentityExporter>();
        services.AddScoped<LocIntel.Contracts.IActorDirectory, Users.ActorDirectory>();
        services.AddScoped<LocIntel.Contracts.IReportRequester, Users.ReportRequester>();
        return services;
    }
}
