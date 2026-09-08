using LocIntel.Platform.Infra;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Api;

/// <summary>
/// Hard-deletes expired idempotency rows (ADR 29; the expired_cleanup RLS
/// policy authorizes exactly this cross-org delete) and prunes sweep leases
/// older than a month - the two platform tables with a TTL.
/// </summary>
public static class CleanupIdempotencyHandler
{
    public static async Task Handle(
        CleanupIdempotency _,
        PlatformDbContext db,
        CancellationToken ct
    )
    {
        if (db.Tenant.OrgId is not null)
            throw new InvalidOperationException("Global cleanup must not carry a tenant");
        // Deliberately no EF tenant filter or SQL WHERE: expired_cleanup is the
        // RLS DELETE predicate. A WHERE on created_at also requires SELECT RLS,
        // which correctly hides all tenant rows from this tenantless job.
        // Reject tenant context above so the ordinary tenant policy cannot also
        // authorize deletion of that tenant's unexpired records.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM platform.idempotency_keys", ct);
        var stale = DateTimeOffset.UtcNow.AddDays(-30);
        await db.SweepRuns.Where(r => r.Period < stale).ExecuteDeleteAsync(ct);
    }
}
