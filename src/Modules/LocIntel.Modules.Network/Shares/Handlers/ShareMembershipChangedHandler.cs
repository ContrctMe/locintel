using LocIntel.Modules.Network.Data;
using LocIntel.Modules.Network.Shares.Messages;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Network.Shares.Handlers;

/// <summary>Runs as the OWNER: a member accepted or left; the roster changes and goes back out to everyone.</summary>
public static class ShareMembershipChangedHandler
{
    [Transactional(typeof(NetworkDbContext))]
    public static async Task Handle(
        ShareMembershipChanged message,
        Envelope envelope,
        ITenantContext tenant,
        NetworkDbContext db,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is null)
            throw new InvalidOperationException(
                $"ShareMembershipChanged arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        var share = await db.Shares.FirstOrDefaultAsync(s => s.Id == message.ShareId, ct);
        var member = await db.Members.FirstOrDefaultAsync(
            m => m.ShareId == message.ShareId && m.MemberOrgId == message.MemberOrgId,
            ct
        );
        if (share is null || member is null || member.Status == MembershipStatus.Removed)
            return;
        // an org may only move its own standing between Invited, Active and Left
        if (message.Status is not (MembershipStatus.Active or MembershipStatus.Left))
            return;
        member.Status = message.Status;
        member.JoinedAt =
            message.Status == MembershipStatus.Active ? message.JoinedAt : member.JoinedAt;
        await db.SaveChangesAsync(ct);
        await ShareRoster.PublishAsync(db, bus, share, ct);
    }
}
