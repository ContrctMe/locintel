using LocIntel.Modules.Marketplace.Data;
using LocIntel.Platform.Data;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.Modules.Marketplace;

public static class MarketplaceModule
{
    public static IServiceCollection AddMarketplaceModule(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        bool runBackgroundWork = false
    )
    {
        services.Configure<Marketplace.MarketplaceOptions>(configuration.GetSection("Marketplace"));
        if (runBackgroundWork)
            services.AddHostedService<Requests.RequestSlaService>();
        services.AddModuleDbContext<MarketplaceDbContext>("marketplace");
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, MarketplaceExporter>();
        return services;
    }
}
