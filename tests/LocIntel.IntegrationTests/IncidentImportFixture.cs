using LocIntel.Modules.Incidents.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

public class IncidentImportFixture : UploadReadFixture
{
    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        base.ConfigureHost(builder);
        builder.ConfigureServices(services =>
            services.ConfigureDbContext<IncidentsDbContext>(options =>
                options.AddInterceptors(new ObserveConnections(Probe))
            )
        );
    }
}
