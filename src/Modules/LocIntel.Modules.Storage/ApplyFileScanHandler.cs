using LocIntel.Contracts;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Storage;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Storage;

public static class ApplyFileScanHandler
{
    [Transactional(typeof(StorageDbContext))]
    public static async Task Handle(
        ApplyFileScan message,
        StorageDbContext db,
        ITenantContext tenant,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException("ApplyFileScan requires a tenant envelope.");
        if (message.Verdict is not (ScanVerdict.Clean or ScanVerdict.Infected))
            throw new ArgumentOutOfRangeException(nameof(message.Verdict));

        // Lock only the final read/change/outbox unit, never object storage or AV I/O.
        // RLS and the context's tenant filter still apply to this query.
        await db.TakeAsync(message.FileId, ct);
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == message.FileId, ct);
        if (file is null || file.Status != FileStatus.Uploaded || file.Key != message.Key)
            return;

        file.Status =
            message.Verdict == ScanVerdict.Clean ? FileStatus.Clean : FileStatus.Quarantined;
        file.ScannedAt = DateTimeOffset.UtcNow;
        if (file.Status == FileStatus.Clean)
            await bus.PublishAsync(
                new GenerateDerivatives(file.Id),
                new DeliveryOptions { TenantId = org.Value.ToString() }
            );
        else
            await bus.PublishAsync(
                new RecordDomainAudit(
                    "file.quarantined",
                    System.Text.Json.JsonSerializer.Serialize(new { file.Id, file.Name })
                ),
                new DeliveryOptions { TenantId = org.Value.ToString() }
            );
        // Wolverine saves the tracked change and outbox, then commits together.
    }
}
