using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Storage;

public static class StorageModule
{
    public static IServiceCollection AddStorageModule(
        this IServiceCollection services,
        bool runBackgroundWork = false
    )
    {
        if (runBackgroundWork)
        {
            services.AddHostedService<FileTrashService>();
            services.AddHostedService<PendingUploadService>();
        }
        services.AddModuleDbContext<StorageDbContext>("storage");
        services.AddScoped<LocIntel.Contracts.IStoredFileLookup, StoredFileLookup>();
        services.AddScoped<LocIntel.Contracts.Storage.ISignedFileAccess, SignedFileAccess>();
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, StorageExporter>();
        services.AddScoped<LocIntel.Contracts.IReportFileSource, ReportFileSource>();
        services.AddScoped<FileAccess>();
        services.AddScoped<LocIntel.Contracts.IReportPublishedFiles, ReportPublishedFiles>();
        return services;
    }
}
