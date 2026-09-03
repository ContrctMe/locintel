using LocIntel.Modules.Network.Data;
using LocIntel.Modules.Network.Shares.Messages;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Network.Shares.Handlers;

/// <summary>
/// Runs as a MEMBER: refreshes its projection of the share, then re-offers
/// its own active bulletins in that share to every active peer - which is
/// how a newly joined member receives what was published before it joined
/// (copies land once: the copy handler keys on the bulletin id).
/// </summary>
public static class ShareRosterChangedHandler
{
    [Transactional(typeof(NetworkDbContext))]
    public static async Task Handle(
        ShareRosterChanged message,
        Envelope envelope,
        ITenantContext tenant,
        NetworkDbContext db,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is null)
            throw new InvalidOperationException(
                $"ShareRosterChanged arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        var access = await db.Access.FirstOrDefaultAsync(a => a.ShareId == message.ShareId, ct);
        if (access is null)
            return;
        access.ShareName = message.ShareName;
        access.Description = message.Description;
        access.OwnerName = message.OwnerName;
        access.ShareStatus = message.ShareStatus;
        access.RosterJson = message.RosterJson;
        await db.SaveChangesAsync(ct);
        if (access.Status != MembershipStatus.Active)
            return;
        var now = time.GetUtcNow();
        var peers = ShareRoster.ActivePeers(access).ToList();
        var mine = await db
            .Bulletins.Where(b =>
                b.ShareId == message.ShareId && b.WithdrawnAt == null && b.ExpiresAt > now
            )
            .ToListAsync(ct);
        foreach (var bulletin in mine)
            await bus.FanOutAsync(peers, bulletin.Offered(access.ShareName), bulletin.Id);
    }
}
