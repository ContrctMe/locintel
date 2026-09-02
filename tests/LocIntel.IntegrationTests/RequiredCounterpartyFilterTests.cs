using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LocIntel.IntegrationTests;

/// <summary>Test-only entity: the required-counterparty shape with no ceremony.</summary>
public sealed class BilateralProbe : IRequiredCounterpartyScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required OrgId CounterpartyOrgId { get; init; }
}

public sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "platform";

    public DbSet<BilateralProbe> Probes => Set<BilateralProbe>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<BilateralProbe>(b =>
        {
            b.ToTable("bilateral_probe");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.CounterpartyOrgId).HasColumnName("counterparty_org_id");
        });
    }
}

/// <summary>
/// Round-two item 10: the convention must apply the either-side filter to
/// IRequiredCounterpartyScoped, or the interface is decoration. Proven with a
/// real context against a real table, because "it compiles" says nothing
/// about whether a query filter was registered.
/// </summary>
public class RequiredCounterpartyFilterTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Counterparty = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private ProbeDbContext Context(Guid? org)
    {
        var tenant = new TenantContext();
        if (org is { } value)
            tenant.Set(new OrgId(value), RegionId.Default);
        return new ProbeDbContext(
            new DbContextOptionsBuilder<ProbeDbContext>()
                .UseNpgsql(fixture.PostgresConnectionString)
                .Options,
            tenant
        );
    }

    [Fact]
    public async Task Both_parties_see_the_row_and_a_stranger_does_not()
    {
        await using (var admin = new NpgsqlConnection(fixture.PostgresConnectionString))
        {
            await admin.OpenAsync();
            await using var ddl = new NpgsqlCommand(
                """
                DROP TABLE IF EXISTS platform.bilateral_probe;
                CREATE TABLE platform.bilateral_probe (
                    id uuid primary key, org_id uuid not null, counterparty_org_id uuid not null);
                """,
                admin
            );
            await ddl.ExecuteNonQueryAsync();
        }

        await using (var seed = Context(Owner))
        {
            seed.Probes.Add(
                new BilateralProbe
                {
                    Id = Guid.NewGuid(),
                    OrgId = new OrgId(Owner),
                    CounterpartyOrgId = new OrgId(Counterparty),
                }
            );
            await seed.SaveChangesAsync();
        }

        await using (var asOwner = Context(Owner))
            Assert.Equal(1, await asOwner.Probes.CountAsync());

        await using (var asCounterparty = Context(Counterparty))
            Assert.Equal(1, await asCounterparty.Probes.CountAsync());

        await using (var asStranger = Context(Stranger))
            Assert.Equal(0, await asStranger.Probes.CountAsync());

        // and with no tenant resolved at all, the filter matches nothing
        await using (var tenantless = Context(null))
            Assert.Equal(0, await tenantless.Probes.CountAsync());
    }
}
