using LocIntel.Modules.Audit;
using LocIntel.Modules.Audit.Data;
using LocIntel.Platform.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine;

namespace LocIntel.IntegrationTests;

public class AuditPartitionMaintenanceTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static DateTime Month(int offset)
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(offset);
    }

    private static string Table(DateTime month) => $"audit.access_log_y{month:yyyy}m{month:MM}";

    private async Task<object?> Sql(string sql, bool app = false)
    {
        await using var connection = new NpgsqlConnection(
            app ? fixture.AppConnectionString : fixture.PostgresConnectionString
        );
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private Task CreateMonth(DateTime month) =>
        Sql(
            $"CREATE TABLE {Table(month)} PARTITION OF audit.access_log FOR VALUES FROM ('{month:yyyy-MM-dd}') TO ('{month.AddMonths(1):yyyy-MM-dd}')"
        );

    private Task Seed(Guid id, Guid org, DateTime when) =>
        Sql(
            $"INSERT INTO audit.access_log (id, org_id, actor_tier, method, path, status_code, occurred_at) VALUES ('{id}', '{org}', 'system', 'GET', '/partition-test', 200, '{when:yyyy-MM-dd}')"
        );

    private async Task Maintain()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(new MaintainAuditPartitions());
    }

    [Fact]
    public async Task Global_maintenance_refuses_a_tenant_context()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MaintainAuditPartitionsHandler.Handle(
                new MaintainAuditPartitions(),
                scope.ServiceProvider.GetRequiredService<AuditDbContext>(),
                default
            )
        );
    }

    [Fact]
    public async Task Tenant_retention_never_creates_partitions_or_deletes_another_tenants_rows()
    {
        var next = Month(1);
        await Sql(
            $"BEGIN; CREATE TEMP TABLE saved AS SELECT * FROM {Table(next)}; DROP TABLE {Table(next)}; INSERT INTO audit.access_log SELECT * FROM saved; COMMIT;"
        );
        var oldA = Guid.CreateVersion7();
        var oldB = Guid.CreateVersion7();
        var freshA = Guid.CreateVersion7();
        await fixture.SeedAuditChange(fixture.OrgA, oldA, DateTimeOffset.UtcNow.AddDays(-100));
        await fixture.SeedAuditChange(fixture.OrgB, oldB, DateTimeOffset.UtcNow.AddDays(-100));
        await fixture.SeedAuditChange(fixture.OrgA, freshA, DateTimeOffset.UtcNow);
        try
        {
            await fixture.PublishForOrgA(new PurgeAuditData());
            await ApiFixture.WaitUntilAsync(
                async () =>
                    Equals(
                        0L,
                        await Sql($"SELECT count(*) FROM audit.change_log WHERE id = '{oldA}'")
                    ),
                "tenant retention to finish"
            );
            Assert.Equal(
                2L,
                await Sql(
                    $"SELECT count(*) FROM audit.change_log WHERE id IN ('{oldB}', '{freshA}')"
                )
            );
            Assert.Equal(DBNull.Value, await Sql($"SELECT to_regclass('{Table(next)}')::text"));
        }
        finally
        {
            await Maintain();
        }
    }

    [Fact]
    public async Task Concurrent_maintenance_recovers_default_rows_and_prunes_only_empty_old_partitions()
    {
        var next = Month(1);
        var populated = Month(-18); // older than 400 days but inside Scale's 730-day window
        var empty = Month(-20);
        await Sql($"DROP TABLE {Table(next)}"); // isolated fixture; no future access rows yet
        await CreateMonth(populated);
        await CreateMonth(empty);
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var old = Guid.CreateVersion7();
        var fallback = Guid.CreateVersion7();
        await Seed(a, fixture.OrgA.Value, next);
        await Seed(b, fixture.OrgB.Value, next);
        await Seed(old, fixture.OrgB.Value, populated);
        await Seed(fallback, fixture.OrgB.Value, Month(-30));

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Maintain()));

        Assert.Equal(2L, await Sql($"SELECT count(*) FROM {Table(next)}"));
        Assert.Equal(1L, await Sql($"SELECT count(*) FROM {Table(populated)}"));
        Assert.Equal(DBNull.Value, await Sql($"SELECT to_regclass('{Table(empty)}')::text"));
        Assert.Equal(
            1L,
            await Sql($"SELECT count(*) FROM audit.access_log_default WHERE id = '{fallback}'")
        );
        Assert.Equal(
            true,
            await Sql(
                $"SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid = '{Table(next)}'::regclass"
            )
        );
        // Give direct SELECT solely to prove the new partition's own RLS,
        // not merely permission denial or parent-table filtering.
        await Sql($"GRANT SELECT ON {Table(next)} TO app_user");
        Assert.Equal(0L, await Sql($"SELECT count(*) FROM {Table(next)}", app: true));
        Assert.Equal(
            1L,
            await Sql(
                $"SET app.org_id = '{fixture.OrgA.Value}'; SELECT count(*) FROM {Table(next)}",
                app: true
            )
        );
    }

    [Fact]
    public async Task Failed_pruning_rolls_back_partition_creation_and_retry_preserves_rows()
    {
        var next = Month(1);
        // The fixture is shared by this class; preserve rows while making the
        // month absent again, regardless of which test executes first.
        await Sql(
            $"BEGIN; CREATE TEMP TABLE saved AS SELECT * FROM {Table(next)}; DROP TABLE {Table(next)}; INSERT INTO audit.access_log SELECT * FROM saved; COMMIT;"
        );
        var id = Guid.CreateVersion7();
        await Seed(id, fixture.OrgA.Value, next);
        await Sql("REVOKE EXECUTE ON FUNCTION audit.prune_access_log_partitions(int) FROM PUBLIC");
        try
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(Maintain);
            Assert.Contains("42501", error.ToString());
            Assert.Equal(DBNull.Value, await Sql($"SELECT to_regclass('{Table(next)}')::text"));
            Assert.Equal(
                1L,
                await Sql($"SELECT count(*) FROM audit.access_log_default WHERE id = '{id}'")
            );
        }
        finally
        {
            await Sql("GRANT EXECUTE ON FUNCTION audit.prune_access_log_partitions(int) TO PUBLIC");
        }
        await Maintain();
        Assert.Equal(1L, await Sql($"SELECT count(*) FROM {Table(next)} WHERE id = '{id}'"));
        Assert.Equal(
            0L,
            await Sql($"SELECT count(*) FROM audit.access_log_default WHERE id = '{id}'")
        );
    }
}
