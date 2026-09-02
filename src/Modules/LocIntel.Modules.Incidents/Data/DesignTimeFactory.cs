using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LocIntel.Modules.Incidents.Data;

/// <summary>Design-time only (dotnet ef). Never used at runtime.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<IncidentsDbContext>
{
    public IncidentsDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<IncidentsDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=design_time_only",
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "incidents")
                )
                .Options,
            new TenantContext()
        );
}
