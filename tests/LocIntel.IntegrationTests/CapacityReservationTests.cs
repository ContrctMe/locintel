using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Ingest.Data;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace LocIntel.IntegrationTests;

public class CapacityReservationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private IServiceScope Scope(OrgId org)
    {
        var scope = fixture.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(org, RegionId.Default);
        return scope;
    }

    private async Task<(HttpClient Client, OrgId Org, Guid Root)> Setup()
    {
        var name = "capacity-" + Guid.NewGuid().ToString("N");
        var client = await fixture.LoginAsync(name + "@locintel.local");
        var response = await client.PostAsJsonAsync("/api/orgs", new { name, slug = name });
        response.EnsureSuccessStatusCode();
        var org = new OrgId(
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orgId").GetGuid()
        );
        await ApiFixture.WaitForMembershipAsync(client);
        (
            await client.PostAsJsonAsync("/auth/switch-org", new { orgId = org.Value })
        ).EnsureSuccessStatusCode();
        var root = await ApiFixture.WaitForRootAsync(client);
        var op = await fixture.LoginAsync(ApiFixture.Operator);
        (
            await op.PutAsJsonAsync(
                $"/api/operator/orgs/{org.Value}/entitlements/sites.max",
                new { value = "1" }
            )
        ).EnsureSuccessStatusCode();
        return (client, org, root);
    }

    private async Task<Guid> Batch(OrgId org, Guid root, int count)
    {
        using var scope = Scope(org);
        var db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();
        var id = Guid.NewGuid();
        db.Batches.Add(
            new ImportBatch
            {
                Id = id,
                OrgId = org,
                Source = "capacity-test",
                CreatedBy = Guid.NewGuid(),
            }
        );
        for (var i = 0; i < count; i++)
            db.StagedSites.Add(
                new StagedSite
                {
                    Id = Guid.NewGuid(),
                    OrgId = org,
                    BatchId = id,
                    ExternalId = $"{id:N}-{i}",
                    Name = $"Imported {i}",
                    TimeZone = "Etc/UTC",
                    NodePath = "",
                    NodeId = root,
                    SourceStatus = "open",
                    Action = "create",
                }
            );
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<HttpResponseMessage> RetryBusy(
        Func<Task<HttpResponseMessage>> request
    )
    {
        HttpResponseMessage response = null!;
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                response?.Dispose();
                response = await request();
                return response.StatusCode
                    is not (HttpStatusCode.ServiceUnavailable or HttpStatusCode.Conflict);
            },
            "capacity contention to clear"
        );
        return response;
    }

    [Fact]
    public async Task Competing_imports_and_direct_create_cannot_exceed_one_slot()
    {
        var (client, org, root) = await Setup();
        var first = await Batch(org, root, 1);
        var second = await Batch(org, root, 1);
        var responses = await Task.WhenAll(
            RetryBusy(() => client.PostAsync($"/api/ingest/batches/{first}/commit", null)),
            RetryBusy(() => client.PostAsync($"/api/ingest/batches/{second}/commit", null)),
            RetryBusy(() =>
                client.PostAsJsonAsync(
                    "/api/sites",
                    new
                    {
                        nodeId = root,
                        name = "Direct",
                        timeZone = "Etc/UTC",
                    }
                )
            )
        );
        Assert.Single(responses, r => r.IsSuccessStatusCode);
        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.PaymentRequired));
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                using var scope = Scope(org);
                var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
                return await db.Sites.LongCountAsync() == 1
                    && await CapacityReservations.PendingAsync(
                        db,
                        org,
                        EntitlementCatalog.MaxSites,
                        default
                    ) == 0;
            },
            "the sole accepted create to finish and consume capacity"
        );
    }

    [Fact]
    public async Task Over_limit_batch_rejects_every_row_and_reserves_nothing()
    {
        var (client, org, root) = await Setup();
        var batch = await Batch(org, root, 2);
        Assert.Equal(
            HttpStatusCode.PaymentRequired,
            (await client.PostAsync($"/api/ingest/batches/{batch}/commit", null)).StatusCode
        );
        using var scope = Scope(org);
        var db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();
        Assert.Equal(BatchStatus.Staged, (await db.Batches.SingleAsync()).Status);
        Assert.Equal(
            0,
            await CapacityReservations.PendingAsync(db, org, EntitlementCatalog.MaxSites, default)
        );
        Assert.Equal(
            0,
            await scope.ServiceProvider.GetRequiredService<TenancyDbContext>().Sites.CountAsync()
        );
    }

    [Fact]
    public async Task Pending_work_blocks_direct_creation_and_consumes_once_on_replay()
    {
        var (client, org, root) = await Setup();
        var reservation = Guid.NewGuid();
        using (var scope = Scope(org))
        {
            var db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            Assert.True(
                await CapacityReservations.TryLockAsync(
                    db,
                    org,
                    EntitlementCatalog.MaxSites,
                    default
                )
            );
            await CapacityReservations.ReserveAsync(
                db,
                org,
                EntitlementCatalog.MaxSites,
                Guid.NewGuid(),
                [reservation],
                default
            );
            await tx.CommitAsync();
        }
        Assert.Equal(
            HttpStatusCode.PaymentRequired,
            (
                await client.PostAsJsonAsync(
                    "/api/sites",
                    new
                    {
                        nodeId = root,
                        name = "Blocked",
                        timeZone = "Etc/UTC",
                    }
                )
            ).StatusCode
        );
        var message = new SiteChangeRequested(
            "create",
            "reserved",
            "Reserved",
            "Etc/UTC",
            root,
            reservation
        );
        using (var scope = Scope(org))
        {
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.PublishAsync(
                message,
                new DeliveryOptions { TenantId = org.Value.ToString() }
            );
            await bus.PublishAsync(
                message,
                new DeliveryOptions { TenantId = org.Value.ToString() }
            );
        }
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                using var scope = Scope(org);
                var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
                return await db.Sites.CountAsync() == 1
                    && await CapacityReservations.PendingAsync(
                        db,
                        org,
                        EntitlementCatalog.MaxSites,
                        default
                    ) == 0;
            },
            "reserved work to complete"
        );
        // Replay synchronously as well, so completion of both attempts is observed.
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                using var replay = Scope(org);
                try
                {
                    await replay
                        .ServiceProvider.GetRequiredService<IMessageBus>()
                        .InvokeForTenantAsync(org.Value.ToString(), message);
                    return true;
                }
                catch (CapacityBusyException)
                {
                    return false;
                }
            },
            "the explicit replay to complete after concurrent delivery"
        );
        using var verify = Scope(org);
        Assert.Equal(
            1,
            await verify.ServiceProvider.GetRequiredService<TenancyDbContext>().Sites.CountAsync()
        );
    }

    [Fact]
    public async Task Capacity_writes_refuse_missing_transaction()
    {
        using var scope = Scope(fixture.OrgA);
        var db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CapacityReservations.TryLockAsync(
                db,
                fixture.OrgA,
                EntitlementCatalog.MaxSites,
                default
            )
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CapacityReservations.ReserveAsync(
                db,
                fixture.OrgA,
                EntitlementCatalog.MaxSites,
                Guid.NewGuid(),
                [Guid.NewGuid()],
                default
            )
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CapacityReservations.ConsumeAsync(
                db,
                fixture.OrgA,
                EntitlementCatalog.MaxSites,
                Guid.NewGuid(),
                default
            )
        );
    }

    [Fact]
    public async Task Reservation_rollback_and_database_tenant_isolation_hold()
    {
        using var scope = Scope(fixture.OrgA);
        var db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();
        var committed = Guid.NewGuid();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await CapacityReservations.ReserveAsync(
                db,
                fixture.OrgA,
                EntitlementCatalog.MaxSites,
                Guid.NewGuid(),
                [committed],
                default
            );
            await tx.CommitAsync();
        }
        using (var other = Scope(fixture.OrgB))
            Assert.Equal(
                0,
                await CapacityReservations.PendingAsync(
                    other.ServiceProvider.GetRequiredService<IngestDbContext>(),
                    fixture.OrgA,
                    EntitlementCatalog.MaxSites,
                    default
                )
            );
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await CapacityReservations.ReserveAsync(
                db,
                fixture.OrgA,
                EntitlementCatalog.MaxSites,
                Guid.NewGuid(),
                [Guid.NewGuid()],
                default
            );
            Assert.Equal(
                2,
                await CapacityReservations.PendingAsync(
                    db,
                    fixture.OrgA,
                    EntitlementCatalog.MaxSites,
                    default
                )
            );
            await tx.RollbackAsync();
        }
        Assert.Equal(
            1,
            await CapacityReservations.PendingAsync(
                db,
                fixture.OrgA,
                EntitlementCatalog.MaxSites,
                default
            )
        );
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                CapacityReservations.ReserveAsync(
                    db,
                    fixture.OrgB,
                    EntitlementCatalog.MaxSites,
                    Guid.NewGuid(),
                    [Guid.NewGuid()],
                    default
                )
            );
            Assert.Equal("42501", error.SqlState);
            await tx.RollbackAsync();
        }
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            Assert.Equal(
                1,
                await CapacityReservations.ConsumeAsync(
                    db,
                    fixture.OrgA,
                    EntitlementCatalog.MaxSites,
                    committed,
                    default
                )
            );
            await tx.CommitAsync();
        }
    }
}
