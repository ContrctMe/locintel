using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Platform.Data;

/// <summary>
/// Serializes the handlers that project ONE aggregate (ADR 48 materialization):
/// a transaction-scoped advisory lock keyed on the aggregate's id. Without
/// it, two copies of a fan-out or two quick successive events for the same
/// aggregate are handled in parallel by the local queue, both miss each
/// other's uncommitted row, and the second dies on the unique index - a
/// retry that lands late instead of a write that lands once. Lifted from a
/// fork, where fourteen projection handlers take it.
///
/// Take it FIRST THING in the handler. Wolverine's transactional middleware
/// has already opened the transaction, so the lock lives exactly as long as
/// the work and releases on commit or rollback. Outside a transaction the
/// lock would release at the end of its own statement - a silent no-op -
/// so it is refused instead.
/// </summary>
public static class AggregateLock
{
    /// <summary>Lock one aggregate: an owner-side row, or a platform-global projection keyed by id.</summary>
    public static Task TakeAsync(this DbContext db, Guid aggregateId, CancellationToken ct) =>
        TakeAsync(db, aggregateId.ToString("N"), ct);

    /// <summary>
    /// Lock one org's copy of an aggregate: a recipient row materialized from
    /// a fan-out is keyed on (correlation, own org), so lock on the same pair
    /// and fifty recipients of one request do not serialize with each other.
    /// </summary>
    public static Task TakeAsync(
        this DbContext db,
        OrgId org,
        Guid aggregateId,
        CancellationToken ct
    ) => TakeAsync(db, $"{org.Value:N}:{aggregateId:N}", ct);

    private static Task TakeAsync(DbContext db, string key, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "AggregateLock.TakeAsync needs an open transaction: pg_advisory_xact_lock releases "
                    + "at the end of its own statement otherwise. Take it inside a Wolverine "
                    + "transactional handler or after BeginTransactionAsync."
            );
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))",
            ct
        );
    }
}
