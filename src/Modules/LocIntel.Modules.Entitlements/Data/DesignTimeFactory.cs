using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Entitlements.Data;

/// <summary>Design-time only (dotnet ef). Never used at runtime.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<EntitlementsDbContext>
{
    public EntitlementsDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<EntitlementsDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=design_time_only",
                    npgsql =>
                        npgsql.MigrationsHistoryTable("__ef_migrations_history", "entitlements")
                )
                .Options,
            new TenantContext()
        );
}
