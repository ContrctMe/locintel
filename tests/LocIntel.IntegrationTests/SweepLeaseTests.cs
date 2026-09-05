using LocIntel.Api;
using LocIntel.Modules.Audit;
using LocIntel.Modules.Entitlements;
using LocIntel.Modules.Tenancy.Organizations;
using LocIntel.Platform.Infra;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace LocIntel.IntegrationTests;

/// <summary>
/// N worker replicas, one logical sweep per period (docs/production.md).
/// The maturity review's release blocker: every replica started the same
/// timers with nothing at the scheduling seam to stop duplicate sweeps.
/// </summary>
public class SweepLeaseTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private PlatformDbContext Platform() =>
        (PlatformDbContext)
            ApiFixture.CreateCatalogContext(
                ModuleCatalog.Platform,
                fixture.PostgresConnectionString
            );

    [Fact]
    public async Task Concurrent_claims_on_one_period_grant_exactly_one()
    {
        var sweep = $"probe-{Guid.NewGuid():N}";
        var claims = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(async _ =>
                {
                    using var scope = fixture.Factory.Services.CreateScope();
                    var lease = scope.ServiceProvider.GetRequiredService<ISweepLease>();
                    return await lease.TryClaimAsync(sweep, TimeSpan.FromHours(1));
                })
        );

        Assert.Equal(1, claims.Count(won => won));
    }

    [Fact]
    public async Task The_next_period_is_a_fresh_claim_and_a_late_starter_in_the_same_one_is_not()
    {
        var sweep = $"probe-{Guid.NewGuid():N}";
        using var scope = fixture.Factory.Services.CreateScope();
        var lease = scope.ServiceProvider.GetRequiredService<ISweepLease>();

        Assert.True(await lease.TryClaimAsync(sweep, TimeSpan.FromHours(1)));
        // a replica that starts later in the same hour sees the row and skips
        Assert.False(await lease.TryClaimAsync(sweep, TimeSpan.FromHours(1)));
        // a different interval is a different period key, so it is its own sweep
        Assert.True(await lease.TryClaimAsync(sweep, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Two_worker_replicas_produce_one_sweep_per_period()
    {
        // both workers tick every sweep at start; the lease rows are the
        // durable record of who ran what, and there must be one per sweep
        using var first = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("ROLE", "worker"));
        using var second = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("ROLE", "worker"));
        using var clientA = first.CreateClient();
        using var clientB = second.CreateClient();
        (await clientA.GetAsync("/healthz")).EnsureSuccessStatusCode();
        (await clientB.GetAsync("/healthz")).EnsureSuccessStatusCode();

        string[] sweeps =
        [
            SweepIdentity.For<PurgeAuditData>(),
            SweepIdentity.For<MaintainAuditPartitions>(),
            SweepIdentity.For<CompactMeters>(),
            SweepIdentity.For<ProcessOrgClosure>(),
            SweepIdentity.For<CleanupIdempotency>(),
        ];
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                await using var db = Platform();
                var claimed = await db.SweepRuns.Select(r => r.Sweep).Distinct().ToListAsync();
                return sweeps.All(claimed.Contains);
            },
            "every sweep to be claimed by one of the two workers"
        );

        await using var check = Platform();
        foreach (var sweep in sweeps)
            Assert.Equal(1, await check.SweepRuns.CountAsync(r => r.Sweep == sweep));
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                await using var db = Platform();
                return await db
                        .Database.SqlQueryRaw<long>(
                            "SELECT count(*) AS \"Value\" FROM wolverine.wolverine_incoming_envelopes WHERE message_type = 'LocIntel.Modules.Audit.MaintainAuditPartitions' AND status = 'Handled'"
                        )
                        .SingleAsync() == 1;
            },
            "one durable global audit maintenance message to complete across two workers"
        );
    }

    [Fact]
    public async Task The_cleanup_sweep_has_a_handler_and_it_prunes_expired_rows()
    {
        // a Wolverine message without a discovered handler publishes into the
        // void (CLAUDE.md); this proves the idempotency cleanup found its home
        await using (var seed = Platform())
        {
            foreach (var org in new[] { fixture.OrgA, fixture.OrgB })
            foreach (var expired in new[] { false, true })
                seed.IdempotencyRecords.Add(
                    new IdempotencyRecord
                    {
                        OrgId = org,
                        Key = expired ? "cleanup-expired" : "cleanup-fresh",
                        Endpoint = "test",
                        RequestHash = "test",
                        CreatedAt = DateTimeOffset.UtcNow.AddHours(expired ? -25 : -1),
                    }
                );
            seed.SweepRuns.Add(
                new SweepRun
                {
                    Sweep = "ancient-probe",
                    Period = DateTimeOffset.UtcNow.AddDays(-45),
                    ClaimedAt = DateTimeOffset.UtcNow.AddDays(-45),
                    ClaimedBy = "test",
                }
            );
            await seed.SaveChangesAsync();
        }
        using var scope = fixture.Factory.Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(new CleanupIdempotency());

        await using var db = Platform();
        Assert.False(await db.SweepRuns.AnyAsync(r => r.Sweep == "ancient-probe"));
        var rows = await db
            .IdempotencyRecords.IgnoreQueryFilters()
            .Where(r => r.Key.StartsWith("cleanup-"))
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("cleanup-fresh", row.Key));
        Assert.Contains(rows, row => row.OrgId == fixture.OrgA);
        Assert.Contains(rows, row => row.OrgId == fixture.OrgB);
    }

    [Fact]
    public async Task Global_cleanup_refuses_tenant_context()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CleanupIdempotencyHandler.Handle(new CleanupIdempotency(), db, default)
        );
    }
}
