using LocIntel.Modules.Network.Data;
using LocIntel.Modules.Network.Shares.Messages;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Network.Shares.Handlers;

public static class ShareAccessRevokedHandler
{
    [Transactional(typeof(NetworkDbContext))]
    public static async Task Handle(
        ShareAccessRevoked message,
        Envelope envelope,
        ITenantContext tenant,
        NetworkDbContext db,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"ShareAccessRevoked arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        await db.TakeAsync(org, message.ShareId, ct);
        var access = await db.Access.FirstOrDefaultAsync(a => a.ShareId == message.ShareId, ct);
        if (access is null)
            return;
        access.Status = MembershipStatus.Removed;
        // revocation is deletion (ADR 48): the copies go with the access
        await db.BulletinCopies.Where(c => c.ShareId == message.ShareId).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
    }
}
