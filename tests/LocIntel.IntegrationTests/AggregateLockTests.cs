using LocIntel.Api;
using LocIntel.Contracts;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace LocIntel.IntegrationTests;

/// <summary>
/// AggregateLock (ADR 48 materialization, docs/cross-tenant-sharing.md):
/// projection handlers for one aggregate serialize, so two copies of a
/// fan-out land as one row instead of a unique-index death and a late retry.
/// </summary>
public class AggregateLockTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly TimeSpan NotYet = TimeSpan.FromMilliseconds(500);

    private IdentityDbContext Identity() =>
        (IdentityDbContext)
            ApiFixture.CreateCatalogContext(
                ModuleCatalog.All.Single(m => m.Name == "identity"),
                fixture.PostgresConnectionString
            );

    [Fact]
    public async Task Two_transactions_on_one_aggregate_serialize_and_two_aggregates_do_not()
    {
        var aggregate = Guid.NewGuid();
        await using var first = Identity();
        await using var second = Identity();
        await using var other = Identity();
        await using var tx1 = await first.Database.BeginTransactionAsync();
        await using var tx2 = await second.Database.BeginTransactionAsync();
        await using var tx3 = await other.Database.BeginTransactionAsync();

        await first.TakeAsync(aggregate, CancellationToken.None);
        var contended = second.TakeAsync(aggregate, CancellationToken.None);
        // a different aggregate is not behind the lock
        await other.TakeAsync(Guid.NewGuid(), CancellationToken.None).WaitAsync(NotYet);

        // the same aggregate is: the second waits for the first to finish. A
        // completed task here is either a fault (surface it, with its message)
        // or a lock that did not hold.
        if (await Task.WhenAny(contended, Task.Delay(NotYet)) == contended)
        {
            await contended;
            Assert.Fail("the second transaction took the lock while the first still held it");
        }

        await tx1.CommitAsync(); // ...and proceeds the moment it does
        await contended.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task An_orgs_copy_of_an_aggregate_is_its_own_lock()
    {
        // recipients of one fan-out lock on (own org, correlation): two orgs
        // materializing the same request do not serialize with each other
        var request = Guid.NewGuid();
        await using var a = Identity();
        await using var b = Identity();
        await using var txA = await a.Database.BeginTransactionAsync();
        await using var txB = await b.Database.BeginTransactionAsync();

        await a.TakeAsync(fixture.OrgA, request, CancellationToken.None);
        await b.TakeAsync(fixture.OrgB, request, CancellationToken.None).WaitAsync(NotYet);
    }

    [Fact]
    public async Task Outside_a_transaction_the_lock_is_refused_rather_than_silently_useless()
    {
        // pg_advisory_xact_lock would release at the end of its own statement
        await using var db = Identity();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.TakeAsync(Guid.NewGuid(), CancellationToken.None)
        );
    }

    [Fact]
    public async Task Two_events_for_one_aggregate_handled_concurrently_land_as_one_row()
    {
        // through the real outbox and the real projection handler: the lock
        // runs inside Wolverine's transaction (the guard would throw otherwise)
        // and two concurrent upserts of a fresh org converge on one row
        var org = OrgId.New();
        using var scope = fixture.Factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await Task.WhenAll(
            bus.PublishAsync(
                    new OrganizationUpserted(
                        org,
                        "First",
                        $"lock-{org.Value:N}",
                        RegionId.Default,
                        null
                    )
                )
                .AsTask(),
            bus.PublishAsync(
                    new OrganizationUpserted(
                        org,
                        "Second",
                        $"lock-{org.Value:N}",
                        RegionId.Default,
                        null
                    )
                )
                .AsTask()
        );

        var row = await ApiFixture.WaitForAsync(
            async () =>
            {
                await using var db = Identity();
                return await db.OrgDirectory.FirstOrDefaultAsync(d => d.OrgId == org);
            },
            "the org directory row for the doubly-upserted org"
        );
        Assert.Contains(row.Name, new[] { "First", "Second" });
        await using var check = Identity();
        Assert.Equal(1, await check.OrgDirectory.CountAsync(d => d.OrgId == org));
    }
}
