using LocIntel.Modules.Network.Data;
using LocIntel.Platform.Data;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.Modules.Network;

public static class NetworkModule
{
    public static IServiceCollection AddNetworkModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<NetworkDbContext>("network");
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, NetworkExporter>();
        return services;
    }
}
