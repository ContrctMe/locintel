using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Platform.Entitlements;

/// <summary>
/// Pending capacity is written through the caller's transaction, alongside its
/// durable intent. Platform owns the table; modules retain their own DbContexts.
/// Reservations have no TTL: delayed or dead-lettered work still owns its capacity.
/// </summary>
public static class CapacityReservations
{
    // Fail fast instead of filling the pool with lock waiters. The winner may
    // need a second module connection to read authoritative usage.
    public static Task<bool> TryLockAsync(
        DbContext db,
        OrgId org,
        string code,
        CancellationToken ct
    )
    {
        RequireTransaction(db);
        var key = $"capacity:{org.Value:N}:{code}";
        return db
            .Database.SqlQuery<bool>(
                $"SELECT pg_try_advisory_xact_lock(hashtextextended({key}, 0)) AS \"Value\""
            )
            .SingleAsync(ct);
    }

    public static Task<long> PendingAsync(
        DbContext db,
        OrgId org,
        string code,
        CancellationToken ct
    ) =>
        db
            .Database.SqlQuery<long>(
                $"SELECT count(*) AS \"Value\" FROM platform.capacity_reservations WHERE org_id = {org.Value} AND code = {code}"
            )
            .SingleAsync(ct);

    public static Task ReserveAsync(
        DbContext db,
        OrgId org,
        string code,
        Guid batchId,
        Guid[] rows,
        CancellationToken ct
    )
    {
        RequireTransaction(db);
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO platform.capacity_reservations (id, org_id, code, batch_id, created_at) SELECT id, {org.Value}, {code}, {batchId}, now() FROM unnest({rows}) AS id",
            ct
        );
    }

    public static Task<int> ConsumeAsync(
        DbContext db,
        OrgId org,
        string code,
        Guid id,
        CancellationToken ct
    )
    {
        RequireTransaction(db);
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM platform.capacity_reservations WHERE id = {id} AND org_id = {org.Value} AND code = {code}",
            ct
        );
    }

    private static void RequireTransaction(DbContext db)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Capacity admission requires an open transaction.");
    }
}

/// <summary>Pending durable work; hard-deleted when consumed or explicitly cancelled with its work.</summary>
public sealed class CapacityReservation : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Code { get; init; }
    public required Guid BatchId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Transient admission contention; durable work is rescheduled, never discarded.</summary>
public sealed class CapacityBusyException : Exception
{
    public CapacityBusyException()
        : base("Site capacity is busy; retry the durable operation.") { }
}
