using LocIntel.Contracts;
using LocIntel.Modules.Network.Data;
using LocIntel.Modules.Network.Shares.Messages;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Network.Shares.Handlers;

public static class ShareInvitationRequestedHandler
{
    [Transactional(typeof(NetworkDbContext))]
    public static async Task Handle(
        ShareInvitationRequested message,
        Envelope envelope,
        ITenantContext tenant,
        NetworkDbContext db,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"ShareInvitationRequested arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        var existing = await db.Access.FirstOrDefaultAsync(a => a.ShareId == message.ShareId, ct);
        if (existing is null)
        {
            existing = new ShareAccess
            {
                Id = Guid.CreateVersion7(),
                OrgId = org,
                ShareId = message.ShareId,
                OwnerOrgId = message.OwnerOrgId,
                ShareName = message.ShareName,
                OwnerName = message.OwnerName,
                Role = MemberRole.Member,
                InvitedBy = message.InvitedBy,
            };
            db.Access.Add(existing);
        }
        else if (existing.Status is MembershipStatus.Left or MembershipStatus.Removed)
        {
            existing.Status = MembershipStatus.Invited;
            existing.JoinedAt = null;
        }
        existing.ShareName = message.ShareName;
        existing.Description = message.Description;
        existing.OwnerName = message.OwnerName;
        existing.ShareStatus = message.ShareStatus;
        existing.RosterJson = message.RosterJson;
        await db.SaveChangesAsync(ct);
        await bus.PublishForOrgAsync(
            org,
            new SendOrgNotice(
                $"Invitation to share intelligence: {message.ShareName}",
                [
                    $"{message.OwnerName} invited your organization to join \"{message.ShareName}\".",
                    "Accept it in the console under Network to start seeing shared bulletins.",
                ],
                "network",
                $"Invitation to share intelligence: {message.ShareName}"
            )
        );
    }
}
