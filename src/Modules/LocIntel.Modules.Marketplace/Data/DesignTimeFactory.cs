using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LocIntel.Modules.Marketplace.Data;

/// <summary>Design-time only (dotnet ef). Never used at runtime.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<MarketplaceDbContext>
{
    public MarketplaceDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<MarketplaceDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=design_time_only",
                    npgsql =>
                        npgsql.MigrationsHistoryTable("__ef_migrations_history", "marketplace")
                )
                .Options,
            new TenantContext()
        );
}
