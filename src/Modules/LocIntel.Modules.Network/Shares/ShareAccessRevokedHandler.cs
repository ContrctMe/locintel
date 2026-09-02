using LocIntel.Modules.Network.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Network.Shares;

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
        if (tenant.OrgId is null)
            throw new InvalidOperationException(
                $"ShareAccessRevoked arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        var access = await db.Access.FirstOrDefaultAsync(a => a.ShareId == message.ShareId, ct);
        if (access is null)
            return;
        access.Status = MembershipStatus.Removed;
        await db.SaveChangesAsync(ct);
    }
}
