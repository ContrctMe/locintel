using LocIntel.Modules.Network.Bulletins.Messages;
using LocIntel.Modules.Network.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Network.Bulletins.Handlers;

public static class SharedBulletinWithdrawnHandler
{
    [Transactional(typeof(NetworkDbContext))]
    public static async Task Handle(
        SharedBulletinWithdrawn m,
        Envelope envelope,
        ITenantContext tenant,
        NetworkDbContext db,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is null)
            throw new InvalidOperationException(
                $"SharedBulletinWithdrawn arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        var copy = await db.BulletinCopies.FirstOrDefaultAsync(
            c => c.BulletinId == m.BulletinId,
            ct
        );
        if (copy is null || copy.WithdrawnAt is not null)
            return;
        copy.WithdrawnAt = m.WithdrawnAt;
        await db.SaveChangesAsync(ct);
    }
}
