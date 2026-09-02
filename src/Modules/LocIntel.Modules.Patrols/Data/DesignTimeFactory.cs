using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LocIntel.Modules.Patrols.Data;

/// <summary>Design-time only (dotnet ef). Never used at runtime.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<PatrolsDbContext>
{
    public PatrolsDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<PatrolsDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=design_time_only",
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "patrols")
                )
                .Options,
            new TenantContext()
        );
}
