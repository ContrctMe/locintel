using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LocIntel.Modules.Spatial.Data;

/// <summary>Design-time only (dotnet ef). Never used at runtime.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<SpatialDbContext>
{
    public SpatialDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SpatialDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=design_time_only",
                npgsql =>
                    LocIntel.Platform.Data.ModulePersistence.Configure(
                        npgsql,
                        "spatial",
                        typeof(SpatialDbContext)
                    )
            )
            .Options;
        return new SpatialDbContext(options, new TenantContext());
    }
}
