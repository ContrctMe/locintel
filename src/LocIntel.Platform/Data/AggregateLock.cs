using Microsoft.EntityFrameworkCore;

namespace LocIntel.Platform.Data;

/// <summary>
/// Serializes the handlers that project ONE aggregate (ADR 48 materialization):
/// a transaction-scoped advisory lock keyed on the aggregate's id. Without
/// it, two copies of a fan-out or two quick successive events for the same
/// request are handled in parallel by the local queue, both miss each
/// other's uncommitted row, and the second dies on the unique index - a
/// retry that lands late instead of a write that lands once. Take it first
/// thing in the handler: Wolverine's transactional middleware has already
/// opened the transaction, so the lock lives exactly as long as the work.
/// </summary>
public static class AggregateLock
{
    public static Task TakeAsync(this DbContext db, Guid aggregateId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({aggregateId.ToString()}, 0))",
            ct
        );
}
