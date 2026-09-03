using LocIntel.Modules.Marketplace.Data;
using LocIntel.Modules.Marketplace.Requests.Messages;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Marketplace.Requests.Handlers;

/// <summary>Runs as the VENDOR: the requester's state overwrites the assignment's snapshot.</summary>
public static class RequestStateChangedHandler
{
    [Transactional(typeof(MarketplaceDbContext))]
    public static async Task Handle(
        RequestStateChanged message,
        Envelope envelope,
        ITenantContext tenant,
        MarketplaceDbContext db,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is null)
            throw new InvalidOperationException(
                $"RequestStateChanged arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        var row = await db.Assignments.FirstOrDefaultAsync(
            a => a.RequestId == message.Request.RequestId,
            ct
        );
        if (row is null)
            return; // never offered to this org: nothing to update
        row.Apply(message.Request);
        await db.SaveChangesAsync(ct);
    }
}
