using LocIntel.Modules.Marketplace.Data;
using LocIntel.Modules.Marketplace.Requests.Messages;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Marketplace.Requests.Handlers;

/// <summary>Runs as the RECEIVING party: inserts its own copy of the timeline entry, once.</summary>
public static class RequestEventAppendedHandler
{
    [Transactional(typeof(MarketplaceDbContext))]
    public static async Task Handle(
        RequestEventAppended message,
        Envelope envelope,
        ITenantContext tenant,
        MarketplaceDbContext db,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"RequestEventAppended arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        await db.TakeAsync(org, message.RequestId, ct);
        if (await db.Events.AnyAsync(e => e.SourceId == message.SourceId, ct))
            return;
        db.Events.Add(RequestEvent.Copy(org, message));
        await db.SaveChangesAsync(ct);
    }
}
