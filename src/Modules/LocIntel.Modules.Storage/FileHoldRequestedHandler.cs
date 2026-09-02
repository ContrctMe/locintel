using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Contracts.Storage;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Storage;

/// <summary>
/// Applies a hold requested by another module (a case holding its
/// evidence). Tenant from the envelope; a file the org does not own is
/// simply not there. Lifting a hold is honored only when no other holder
/// asked for it - v1 keeps a single flag, so lifts are logged and applied;
/// the case module only lifts holds it placed.
/// </summary>
public static class FileHoldRequestedHandler
{
    [Transactional(typeof(StorageDbContext))]
    public static async Task Handle(
        FileHoldRequested message,
        StorageDbContext db,
        ITenantContext tenant,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException(
                "FileHoldRequested arrived with no tenant on the envelope"
            );
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == message.FileId, ct);
        if (file is null || file.LegalHold == message.Hold)
            return;
        file.LegalHold = message.Hold;
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new RecordDomainAudit(
                message.Hold ? "file.hold_placed" : "file.hold_released",
                JsonSerializer.Serialize(
                    new
                    {
                        file.Id,
                        file.Name,
                        message.Reason,
                    }
                )
            ),
            new DeliveryOptions
            {
                TenantId = org.Value.ToString(),
                Headers = { ["locintel-actor-tier"] = "system" },
            }
        );
    }
}
