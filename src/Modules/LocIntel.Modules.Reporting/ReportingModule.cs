using LocIntel.Modules.Reporting.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Reporting;

public static class ReportingModule
{
    public static IServiceCollection AddReportingModule(
        this IServiceCollection services,
        bool runBackgroundWork = false
    )
    {
        // persistence is built in ONE place (ModulePersistence, ADR 35): the
        // schema is the only fact a module supplies
        services.AddModuleDbContext<ReportingDbContext>("reporting");
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, ReportingExporter>();
        services.AddScoped<ReportRegistry>();
        services.AddScoped<ReportAccess>();
        services.AddScoped<LocIntel.Platform.Storage.IFileOriginAccess, ReportFileAccess>();
        services.AddScoped<ReportQuota>();
        services.AddScoped<LocIntel.Contracts.IEntitlementUsageProbe, ReportQuota>();
        services.AddScoped<ReferenceReportAccess>();
        services.AddSingleton<Rendering.ReportBasemaps>();
        services
            .AddHttpClient(Rendering.ReportBasemaps.ClientName)
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(Rendering.ReportBasemaps.CreateHandler);
        services.AddScoped<Rendering.ReportMap>();
        services.AddScoped<Rendering.ReferenceReportRenderer>();
        services.AddScoped<IReportDefinition, SiteReportDefinition>();
        services.AddScoped<IReportDefinition, AggregateReportDefinition>();
        services.AddSingleton<ReportLimits>();
        services.AddSingleton<ReportRunner>();
        if (runBackgroundWork)
            services.AddHostedService<ReportMaintenanceService>();
        return services;
    }
}
