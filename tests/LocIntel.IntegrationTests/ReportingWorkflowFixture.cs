using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LocIntel.Modules.Reporting;
using LocIntel.Modules.Reporting.Data;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Modules.Tenancy.Sites;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

public sealed class ReportingWorkflowFixture : ApiFixture
{
    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        foreach (var id in new[] { "fixture", "unavailable", "redirect", "corrupt" })
        {
            builder.UseSetting(
                $"Reports:Basemaps:{id}:Url",
                $"https://report-tiles.example.invalid/{id}/{{z}}/{{x}}/{{y}}.png"
            );
            builder.UseSetting(
                $"Reports:Basemaps:{id}:Attribution",
                "Synthetic report tile fixture"
            );
        }
        builder.ConfigureServices(services =>
        {
            services
                .AddSingleton<ConcurrentDictionary<Guid, int>>()
                .AddScoped<IReportDefinition, WorkflowReportDefinition>();
            services.AddTransient<ReportTileFixtureHandler>();
            services
                .AddHttpClient(LocIntel.Modules.Reporting.Rendering.ReportBasemaps.ClientName)
                .ConfigurePrimaryHttpMessageHandler<ReportTileFixtureHandler>();
        });
    }
}
