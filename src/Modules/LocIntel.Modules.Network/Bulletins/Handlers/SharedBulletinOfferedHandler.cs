using LocIntel.Modules.Network.Bulletins.Messages;
using LocIntel.Modules.Network.Data;
using LocIntel.Modules.Network.Shares;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Network.Bulletins.Handlers;

/// <summary>Runs as a MEMBER: its own copy of the bulletin, once, and only while it is active in the share.</summary>
public static class SharedBulletinOfferedHandler
{
    [Transactional(typeof(NetworkDbContext))]
    public static async Task Handle(
        SharedBulletinOffered m,
        Envelope envelope,
        ITenantContext tenant,
        NetworkDbContext db,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"SharedBulletinOffered arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        await db.TakeAsync(org, m.BulletinId, ct);
        var access = await db.Access.FirstOrDefaultAsync(a => a.ShareId == m.ShareId, ct);
        if (access is null || access.Status != MembershipStatus.Active)
            return;
        if (await db.BulletinCopies.AnyAsync(c => c.BulletinId == m.BulletinId, ct))
            return;
        db.BulletinCopies.Add(
            new SharedBulletinCopy
            {
                Id = Guid.CreateVersion7(),
                OrgId = org,
                BulletinId = m.BulletinId,
                ShareId = m.ShareId,
                ShareName = m.ShareName,
                PublisherOrgId = m.PublisherOrgId,
                PublisherName = m.PublisherName,
                Kind = m.Kind,
                Severity = m.Severity,
                Title = m.Title,
                Body = m.Body,
                EntityKind = m.EntityKind,
                DisplayName = m.DisplayName,
                Aliases = m.Aliases,
                DescriptorsJson = m.DescriptorsJson,
                Areas = m.Areas,
                PublishedAt = m.PublishedAt,
                ExpiresAt = m.ExpiresAt,
            }
        );
        await db.SaveChangesAsync(ct);
    }
}
