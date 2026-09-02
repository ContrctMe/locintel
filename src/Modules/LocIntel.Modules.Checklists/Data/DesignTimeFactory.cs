using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LocIntel.Modules.Checklists.Data;

/// <summary>Design-time only (dotnet ef). Never used at runtime.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<ChecklistsDbContext>
{
    public ChecklistsDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<ChecklistsDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=design_time_only",
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "checklists")
                )
                .Options,
            new TenantContext()
        );
}
