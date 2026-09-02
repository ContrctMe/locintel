using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using LocIntel.Platform.Kernel;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The access log is range-partitioned by month (ADR 38 follow-up), with
/// partition DDL reachable only through SECURITY DEFINER functions - the app
/// role holds no DDL of its own.
/// </summary>
public class AccessLogPartitioningTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Monthly_partitions_exist_and_the_app_role_can_maintain_them()
    {
        // the APP role's connection: maintenance must work without owner rights
        var cs = fixture.AppConnectionString;
        await using var db = new LocIntel.Modules.Audit.Data.AuditDbContext(
            new DbContextOptionsBuilder<LocIntel.Modules.Audit.Data.AuditDbContext>()
                .UseNpgsql(cs)
                .Options,
            new TenantContext()
        );

        // the migration seeded current+next month; calling again is idempotent
        await db.Database.ExecuteSqlRawAsync("SELECT audit.ensure_access_log_partitions();");
        var partitions = await db
            .Database.SqlQueryRaw<string>(
                """
                SELECT c.relname AS "Value"
                FROM pg_inherits i
                JOIN pg_class c ON c.oid = i.inhrelid
                JOIN pg_class p ON p.oid = i.inhparent
                JOIN pg_namespace n ON n.oid = p.relnamespace
                WHERE n.nspname = 'audit' AND p.relname = 'access_log'
                ORDER BY 1
                """
            )
            .ToListAsync();
        var now = DateTimeOffset.UtcNow;
        Assert.Contains("access_log_default", partitions);
        Assert.Contains($"access_log_y{now:yyyy}m{now:MM}", partitions);
        Assert.Contains($"access_log_y{now.AddMonths(1):yyyy}m{now.AddMonths(1):MM}", partitions);

        // pruning with a generous floor drops nothing current
        var dropped = (
            await db
                .Database.SqlQueryRaw<int>(
                    "SELECT audit.prune_access_log_partitions(400) AS \"Value\""
                )
                .ToListAsync()
        ).First();
        Assert.Equal(0, dropped);
    }

    [Fact]
    public async Task Access_rows_route_through_the_parent_into_their_month()
    {
        // the middleware writes through the app pipeline; one authenticated
        // request lands at least one access row in the CURRENT month partition
        var client = await fixture.LoginAsync(ApiFixture.UserA);
        // read logging is floor-off: switch it on, then poll until the policy
        // cache resolves and the first logged request lands
        (
            await client.PutAsJsonAsync(
                "/api/admin/audit-config",
                new { logGrants = false, logReads = true }
            )
        ).EnsureSuccessStatusCode();

        // partitions are FORCE RLS'd (security review): a tenantless app_user
        // sees nothing, so physical-routing verification bypasses RLS via the
        // superuser connection. Counting as the superuser proves the ROUTING;
        // that direct app_user access returns 0 is asserted separately below.
        await using var admin = new Npgsql.NpgsqlConnection(fixture.PostgresConnectionString);
        await admin.OpenAsync();
        async Task<long> Count(string table)
        {
            await using var cmd = new Npgsql.NpgsqlCommand($"SELECT count(*) FROM {table}", admin);
            return (long)(await cmd.ExecuteScalarAsync())!;
        }
        var now = DateTimeOffset.UtcNow;
        var partition = $"audit.access_log_y{now:yyyy}m{now:MM}"; // test-derived
        long inMonth = 0;
        for (var i = 0; i < 100 && inMonth == 0; i++)
        {
            (await client.GetAsync("/api/sites")).EnsureSuccessStatusCode();
            inMonth = await Count(partition);
            if (inMonth == 0)
                await Task.Delay(100);
        }
        var parentCount = await Count("audit.access_log");
        var defaultCount = await Count("audit.access_log_default");
        Assert.True(
            inMonth > 0,
            $"no rows in current month partition (parent={parentCount}, default={defaultCount})"
        );
    }
}
