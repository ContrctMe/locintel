using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Platform.Entitlements;

/// <summary>One queued/running data export per organization, shared by audit and organization exports.</summary>
public static class ExportAdmission
{
    public const string Code = "data.export";

    public static async Task<Guid?> TryReserveAsync(DbContext db, OrgId org, CancellationToken ct)
    {
        if (
            !await CapacityReservations.TryLockAsync(db, org, Code, ct)
            || await CapacityReservations.PendingAsync(db, org, Code, ct) != 0
        )
            return null;
        var id = Guid.CreateVersion7();
        await CapacityReservations.ReserveAsync(db, org, Code, id, [id], ct);
        return id;
    }

    public static Task<bool> ExistsAsync(DbContext db, OrgId org, Guid id, CancellationToken ct) =>
        db
            .Database.SqlQuery<bool>(
                $"SELECT EXISTS(SELECT 1 FROM platform.capacity_reservations WHERE org_id = {org.Value} AND code = {Code} AND id = {id}) AS \"Value\""
            )
            .SingleAsync(ct);
}
