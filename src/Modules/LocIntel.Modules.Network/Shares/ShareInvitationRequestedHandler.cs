using LocIntel.Contracts;
using LocIntel.Modules.Network.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Network.Shares;

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
            db.Access.Add(
                new ShareAccess
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = org,
                    ShareId = message.ShareId,
                    ShareName = message.ShareName,
                    Role = MemberRole.Member,
                    InvitedBy = message.InvitedBy,
                }
            );
        }
        else if (existing.Status is MembershipStatus.Left or MembershipStatus.Removed)
        {
            existing.Status = MembershipStatus.Invited;
            existing.JoinedAt = null;
        }
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new SendOrgNotice(
                $"Invitation to share intelligence: {message.ShareName}",
                [
                    $"{message.OwnerName} invited your organization to join \"{message.ShareName}\".",
                    "Accept it in the console under Network to start seeing shared bulletins.",
                ]
            ),
            new DeliveryOptions { TenantId = org.Value.ToString() }
        );
    }
}
