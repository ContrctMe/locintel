using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LocIntel.Modules.Reporting.Data;

/// <summary>Design-time only (dotnet ef). Never used at runtime.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<ReportingDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=design_time_only",
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "reporting")
                )
                .Options,
            new TenantContext()
        );
}
