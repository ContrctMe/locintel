using LocIntel.Modules.Ingest.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Http;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Ingest;

public static class IngestModule
{
    public static IServiceCollection AddIngestModule(
        this IServiceCollection services,
        bool runBackgroundWork = false
    )
    {
        if (runBackgroundWork)
            services.AddHostedService<ConnectorScheduleService>();

        services.AddModuleDbContext<IngestDbContext>("ingest");
        services.AddScoped<StagingService>();
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, IngestExporter>();
        services
            .AddHttpClient("ingest-connector", client => client.Timeout = TimeSpan.FromSeconds(15))
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var environment = sp.GetRequiredService<IHostEnvironment>();
                return PublicHttp.CreateHandler(
                    environment.IsDevelopment() || environment.IsEnvironment("Testing")
                );
            });
        return services;
    }
}
