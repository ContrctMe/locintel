using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LocIntel.Modules.Tenancy.Data;

/// <summary>Design-time only (dotnet ef). Never used at runtime.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<TenancyDbContext>
{
    public TenancyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenancyDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=design_time_only",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "tenancy")
            )
            .Options;
        return new TenancyDbContext(options, new TenantContext());
    }
}
