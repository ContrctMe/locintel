using LocIntel.Platform.Infra;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Platform.Messaging;

/// <summary>
/// The lease as a row: INSERT ... ON CONFLICT DO NOTHING on
/// platform.sweep_runs, keyed on (sweep, period). One statement, no
/// leader, no heartbeat, survives restarts - a replica that starts late in
/// a period sees the row and skips. The window between a claim landing and
/// the sweep's publishes reaching the outbox is not covered: a process that
/// dies exactly there loses that period, which the next period repairs.
/// </summary>
public sealed class SweepLease(PlatformDbContext db, TimeProvider time) : ISweepLease
{
    private static readonly string Owner = $"{Environment.MachineName}:{Environment.ProcessId}";

    public async ValueTask<bool> TryClaimAsync(
        string sweep,
        TimeSpan interval,
        CancellationToken ct = default
    )
    {
        var now = time.GetUtcNow();
        var period = SweepPeriod.Of(now, interval);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO platform.sweep_runs (sweep, period, claimed_at, claimed_by)
            VALUES ({sweep}, {period}, {now}, {Owner})
            ON CONFLICT DO NOTHING
            """,
            ct
        );
        return inserted == 1;
    }
}
