using LocIntel.Modules.Ingest.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace LocIntel.Modules.Ingest;

public static class ApplyConnectorSyncHandler
{
    [Transactional(typeof(IngestDbContext))]
    public static async Task Handle(
        ApplyConnectorSync message,
        IngestDbContext db,
        StagingService staging,
        ITenantContext tenant,
        CancellationToken ct
    )
    {
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException(
                "Connector completion requires a tenant envelope."
            );
        await db.TakeAsync(message.ConnectorId, ct);
        if (
            message.AdmissionId is { } admission
            && !await db
                .Database.SqlQuery<bool>(
                    $"SELECT EXISTS(SELECT 1 FROM platform.capacity_reservations WHERE id = {message.ConnectorId} AND org_id = {org.Value} AND code = {ConnectorQueue.Code} AND batch_id = {admission}) AS \"Value\""
                )
                .SingleAsync(ct)
        )
            return;
        var connector = await db.Connectors.FirstOrDefaultAsync(
            c => c.Id == message.ConnectorId,
            ct
        );
        if (connector is null || await db.Batches.AnyAsync(b => b.Id == message.BatchId, ct))
            return;
        if (ApplyConnectorSync.SnapshotFingerprint(connector) != message.Fingerprint)
            return;

        // The existing staging save and LastSyncedAt are both inside this transaction.
        await staging.StageAsync(
            org,
            Guid.Empty,
            connector.Name,
            message.Rows,
            ct,
            message.BatchId
        );
        connector.LastSyncedAt = DateTimeOffset.UtcNow;
        if (message.AdmissionId is not null)
            await CapacityReservations.ConsumeAsync(db, org, ConnectorQueue.Code, connector.Id, ct);
    }
}
