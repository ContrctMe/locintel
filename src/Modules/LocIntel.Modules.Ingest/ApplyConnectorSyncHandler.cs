using LocIntel.Modules.Ingest.Data;
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
        var connector = await db
            .Connectors.FromSqlInterpolated(
                $"SELECT * FROM ingest.site_connectors WHERE id = {message.ConnectorId} FOR UPDATE"
            )
            .FirstOrDefaultAsync(ct);
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
    }
}
