using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Down() is maintained, not decorative (ADR 38): every module's migrations
/// apply, revert to zero, and apply again against real PostgreSQL. A Down()
/// that does not truly reverse Up() fails here, not in an incident. Own
/// container: this test must control migration state, which the shared
/// fixture (already migrated, already seeded) cannot allow.
/// </summary>
public sealed class MigrationDbFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(
        "postgres:17-alpine"
    ).Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public Task InitializeAsync() => _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();
}

public class MigrationRoundTripTests(MigrationDbFixture fixture) : IClassFixture<MigrationDbFixture>
{
    public static TheoryData<string> Modules =>
        [
            "tenancy",
            "identity",
            "entitlements",
            "audit",
            "storage",
            "platform",
            "ingest",
            "checklists",
            "incidents",
            "entities",
            "cases",
            "marketplace",
            "alerts",
        ];

    [Theory]
    [MemberData(nameof(Modules))]
    public async Task Up_down_up_round_trips(string module)
    {
        await using var db = CreateContext(module);

        await db.Database.MigrateAsync();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.NotEmpty(applied);

        // revert the module entirely; the schema itself stays (it holds the
        // migration history table - an empty schema is the correct end state)
        await db.GetService<IMigrator>().MigrateAsync("0");
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());

        // and forward again: policies, triggers, and raw SQL must all re-apply
        await db.Database.MigrateAsync();
        Assert.Equal(applied, (await db.Database.GetAppliedMigrationsAsync()).ToList());
    }

    private LocIntel.Platform.Data.ModuleDbContext CreateContext(string module)
    {
        var cs = fixture.ConnectionString;
        return module switch
        {
            "tenancy" => Build<LocIntel.Modules.Tenancy.Data.TenancyDbContext>(cs, module),
            "identity" => Build<LocIntel.Modules.Identity.Data.IdentityDbContext>(cs, module),
            "entitlements" => Build<LocIntel.Modules.Entitlements.Data.EntitlementsDbContext>(
                cs,
                module
            ),
            "audit" => Build<LocIntel.Modules.Audit.Data.AuditDbContext>(cs, module),
            "storage" => Build<LocIntel.Modules.Storage.Data.StorageDbContext>(cs, module),
            "platform" => Build<LocIntel.Platform.Infra.PlatformDbContext>(cs, module),
            "ingest" => Build<LocIntel.Modules.Ingest.Data.IngestDbContext>(cs, module),
            "checklists" => Build<LocIntel.Modules.Checklists.Data.ChecklistsDbContext>(cs, module),
            "incidents" => Build<LocIntel.Modules.Incidents.Data.IncidentsDbContext>(cs, module),
            "entities" => Build<LocIntel.Modules.Entities.Data.EntitiesDbContext>(cs, module),
            "cases" => Build<LocIntel.Modules.Cases.Data.CasesDbContext>(cs, module),
            "marketplace" => Build<LocIntel.Modules.Marketplace.Data.MarketplaceDbContext>(
                cs,
                module
            ),
            "alerts" => Build<LocIntel.Modules.Alerts.Data.AlertsDbContext>(cs, module),
            _ => throw new ArgumentOutOfRangeException(nameof(module)),
        };
    }

    private static T Build<T>(string cs, string schema)
        where T : LocIntel.Platform.Data.ModuleDbContext =>
        (T)
            Activator.CreateInstance(
                typeof(T),
                new DbContextOptionsBuilder<T>()
                    .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", schema))
                    .Options,
                new LocIntel.Platform.Kernel.TenantContext()
            )!;
}
